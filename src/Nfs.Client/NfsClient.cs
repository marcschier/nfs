using System.Net;

using Nfs.Abstractions;
using Nfs.Mount;
using Nfs.Protocol.V3;
using Nfs.Protocol.V4;
using Nfs.Rpc;

namespace Nfs.Client;

/// <summary>
/// A high-level, path-based NFS client. It mounts an export and exposes ordinary file operations
/// (stat, read, write, list, create, delete) addressed by slash-separated paths relative to the
/// export root, hiding handles and per-procedure result types. It is a convenience layer over the
/// low-level <see cref="Nfs3Client"/>; use that directly when you need full protocol control.
/// </summary>
public sealed class NfsClient : IAsyncDisposable
{
    private const int ChunkSize = 256 * 1024;
    private static readonly Nfs4Bitmap Nfs4BasicAttributes = Nfs4Bitmap.Of(
        Nfs4AttributeId.Type,
        Nfs4AttributeId.Size,
        Nfs4AttributeId.Mode,
        Nfs4AttributeId.NumLinks,
        Nfs4AttributeId.FileId,
        Nfs4AttributeId.SpaceUsed,
        Nfs4AttributeId.TimeAccess,
        Nfs4AttributeId.TimeModify,
        Nfs4AttributeId.TimeMetadata);

    private readonly IRpcClient _rpc;
    private readonly Nfs3Handle _root3;
    private readonly Nfs4Handle _root4;
    private ulong _clientId;
    private uint _openSequenceId = 1;
    private bool _disposed;

    private NfsClient(IRpcClient rpc, Nfs3Handle root)
    {
        _rpc = rpc;
        Protocol3OrNull = new Nfs3Client(rpc);
        _root3 = root;
        NegotiatedVersion = NfsVersion.V3;
    }

    private NfsClient(IRpcClient rpc, Nfs4Handle root)
    {
        _rpc = rpc;
        Protocol4OrNull = new Nfs4Client(rpc);
        _root4 = root;
        NegotiatedVersion = NfsVersion.V4;
    }

    /// <summary>Gets the NFS protocol version used by this client.</summary>
    public NfsVersion NegotiatedVersion { get; }

    private Nfs3Client? Protocol3OrNull { get; }

    private Nfs4Client? Protocol4OrNull { get; }

    /// <summary>
    /// Connects to a server that hosts both the NFS and MOUNT programs on one endpoint, mounts
    /// <paramref name="exportPath"/>, and returns a ready client.
    /// </summary>
    /// <param name="endPoint">The server endpoint (serving programs 100003 and 100005).</param>
    /// <param name="exportPath">The export to mount.</param>
    /// <param name="cancellationToken">A token to cancel the connect.</param>
    /// <returns>A connected client positioned at the export root.</returns>
    /// <exception cref="NfsException">The export could not be mounted.</exception>
    public static async ValueTask<NfsClient> ConnectAsync(
        EndPoint endPoint,
        string exportPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(exportPath);

        RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var mount = new Mount3Client(rpc);
            Mount3MountResult mounted = await mount.MountAsync(exportPath, cancellationToken).ConfigureAwait(false);
            if (!mounted.IsSuccess)
            {
                throw new NfsException(NfsStatus.NoEntry, $"MOUNT of '{exportPath}' failed: {mounted.Status}.");
            }

            return new NfsClient(rpc, new Nfs3Handle { Data = mounted.Ok.Handle });
        }
        catch
        {
            await rpc.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Connects to an unknown NFS server, selects the highest mutually supported version (v4 then
    /// v3), and returns a ready high-level client positioned at the export root.
    /// </summary>
    /// <param name="endPoint">The server endpoint.</param>
    /// <param name="exportPath">The export path. NFSv4 resolves it with PUTROOTFH and LOOKUP.</param>
    /// <param name="preferred">An optional explicit version pin.</param>
    /// <param name="cancellationToken">A token to cancel the connect.</param>
    /// <returns>A connected client for the selected NFS version.</returns>
    public static async ValueTask<NfsClient> ConnectNegotiatedAsync(
        EndPoint endPoint,
        string exportPath,
        NfsVersion? preferred = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(exportPath);

        IReadOnlySet<uint> supported = await ProbeVersionsAsync(endPoint, cancellationToken).ConfigureAwait(false);
        NfsVersion version = SelectVersion(supported, preferred);
        return version == NfsVersion.V4
            ? await ConnectV4Async(endPoint, exportPath, cancellationToken).ConfigureAwait(false)
            : await ConnectAsync(endPoint, exportPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Probes which NFS major versions a server supports by issuing a NULL call to each and
    /// observing whether it is accepted. Useful for capability discovery before mounting.
    /// </summary>
    /// <param name="endPoint">The server endpoint (serving program 100003).</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns>The set of supported NFS major versions (a subset of {2, 3, 4}).</returns>
    public static async ValueTask<IReadOnlySet<uint>> ProbeVersionsAsync(
        EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        var supported = new HashSet<uint>();
        await using RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        foreach (uint version in new uint[] { 2, 3, 4 })
        {
            RpcReply reply = await rpc.CallAsync(
                100003,
                version,
                procedure: 0,
                OpaqueAuth.None,
                OpaqueAuth.None,
                default(Nfs.Xdr.XdrVoid),
                cancellationToken).ConfigureAwait(false);

            if (reply.IsSuccess)
            {
                supported.Add(version);
            }
        }

        return supported;
    }

    private static async ValueTask<NfsClient> ConnectV4Async(
        EndPoint endPoint,
        string exportPath,
        CancellationToken cancellationToken)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var nfs = new Nfs4Client(rpc);
            Nfs4Handle root = await ResolveV4ExportAsync(nfs, exportPath, cancellationToken).ConfigureAwait(false);
            return new NfsClient(rpc, root);
        }
        catch
        {
            await rpc.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<Nfs4Handle> ResolveV4ExportAsync(
        Nfs4Client nfs,
        string exportPath,
        CancellationToken cancellationToken)
    {
        var operations = new List<Nfs4ArgOp> { new Nfs4PutRootFhOp() };
        foreach (string segment in SplitPath(exportPath))
        {
            operations.Add(new Nfs4LookupOp { Name = segment });
        }

        operations.Add(new Nfs4GetFhOp());
        Nfs4CompoundResult result = await nfs.CompoundAsync("connect", operations, cancellationToken).ConfigureAwait(false);
        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4GetFhResult { Handle: var handle })
        {
            throw new NfsException(MapStatus(result.Status), $"NFSv4 export lookup of '{exportPath}' failed: {result.Status}.");
        }

        return handle;
    }

    private static NfsVersion SelectVersion(IReadOnlySet<uint> supported, NfsVersion? preferred)
    {
        if (preferred is { } pinned)
        {
            uint version = (uint)pinned;
            if ((pinned == NfsVersion.V3 || pinned == NfsVersion.V4) && supported.Contains(version))
            {
                return pinned;
            }

            throw new NfsException(
                NfsStatus.NotSupported,
                $"The server does not support the requested high-level NFS version {version}. Supported versions: {Describe(supported)}.");
        }

        foreach (NfsVersion candidate in new[] { NfsVersion.V4, NfsVersion.V3 })
        {
            if (supported.Contains((uint)candidate))
            {
                return candidate;
            }
        }

        throw new NfsException(
            NfsStatus.NotSupported,
            $"The server does not support any mutually supported high-level NFS version. Supported versions: {Describe(supported)}.");
    }

    private static string Describe(IReadOnlySet<uint> supported) =>
        supported.Count == 0 ? "none" : string.Join(", ", supported.OrderBy(version => version));

    /// <summary>Gets the attributes of the object at <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the export root.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The object's attributes.</returns>
    public async ValueTask<NfsFileAttributes> StatAsync(string path, CancellationToken cancellationToken = default)
    {
        if (NegotiatedVersion == NfsVersion.V4)
        {
            return await StatV4Async(path, cancellationToken).ConfigureAwait(false);
        }

        Nfs3Handle handle = await ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        Nfs3GetAttrResult result = await Protocol3.GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
        return ToAttributes(result.Attributes);
    }

    /// <summary>Reads the entire contents of the file at <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the export root.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file's bytes.</returns>
    public async ValueTask<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (NegotiatedVersion == NfsVersion.V4)
        {
            return await ReadAllBytesV4Async(path, cancellationToken).ConfigureAwait(false);
        }

        Nfs3Handle file = await ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        ulong offset = 0;
        while (true)
        {
            Nfs3ReadResult read = await Protocol3.ReadAsync(file, offset, ChunkSize, cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(read.Status);
            buffer.Write(read.Ok.Data);
            offset += (ulong)read.Ok.Data.Length;
            if (read.Ok.Eof || read.Ok.Data.Length == 0)
            {
                break;
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes <paramref name="data"/> to the file at <paramref name="path"/>, creating it if needed
    /// and truncating it to the new length.
    /// </summary>
    /// <param name="path">A path relative to the export root.</param>
    /// <param name="data">The bytes to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the data is written.</returns>
    public async ValueTask WriteAllBytesAsync(
        string path,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (NegotiatedVersion == NfsVersion.V4)
        {
            await WriteAllBytesV4Async(path, data, cancellationToken).ConfigureAwait(false);
            return;
        }

        (Nfs3Handle parent, string name) = await ResolveParentAsync(path, cancellationToken).ConfigureAwait(false);

        Nfs3Handle file;
        Nfs3LookupResult lookup = await Protocol3.LookupAsync(parent, name, cancellationToken).ConfigureAwait(false);
        if (lookup.IsSuccess)
        {
            file = lookup.Ok.Handle;
            Nfs3WccResult truncate = await Protocol3
                .SetAttributesAsync(file, new Nfs3SetAttributes { Size = 0 }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Throw.IfFailed(truncate.Status);
        }
        else
        {
            Nfs3CreateResult create = await Protocol3
                .CreateAsync(parent, name, Nfs3SetAttributes.None, cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(create.Status);
            file = create.Ok.Handle!.Value;
        }

        for (int written = 0; written < data.Length;)
        {
            int count = Math.Min(ChunkSize, data.Length - written);
            byte[] chunk = data.AsSpan(written, count).ToArray();
            Nfs3WriteResult write = await Protocol3
                .WriteAsync(file, (ulong)written, chunk, Nfs3StableHow.FileSync, cancellationToken)
                .ConfigureAwait(false);
            Throw.IfFailed(write.Status);
            written += (int)write.Ok.Count;
        }
    }

    /// <summary>Lists the entry names in the directory at <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the export root (empty or "/" for the root).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entry names, in the server's order.</returns>
    public async ValueTask<IReadOnlyList<string>> ListAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (NegotiatedVersion == NfsVersion.V4)
        {
            return await ListV4Async(path, cancellationToken).ConfigureAwait(false);
        }

        Nfs3Handle directory = await ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        ulong cookie = 0;
        while (true)
        {
            Nfs3ReadDirResult result = await Protocol3
                .ReadDirectoryAsync(directory, cookie, cancellationToken: cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(result.Status);

            foreach (Nfs3DirEntry entry in result.Ok.Entries)
            {
                names.Add(entry.Name);
                cookie = entry.Cookie;
            }

            if (result.Ok.Eof || result.Ok.Entries.Length == 0)
            {
                break;
            }
        }

        return names;
    }

    /// <summary>Creates a directory at <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the export root.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory is created.</returns>
    public async ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (NegotiatedVersion == NfsVersion.V4)
        {
            await CreateDirectoryV4Async(path, cancellationToken).ConfigureAwait(false);
            return;
        }

        (Nfs3Handle parent, string name) = await ResolveParentAsync(path, cancellationToken).ConfigureAwait(false);
        Nfs3CreateResult result = await Protocol3
            .MakeDirectoryAsync(parent, name, Nfs3SetAttributes.None, cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
    }

    /// <summary>Deletes the file or empty directory at <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the export root.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the object is removed.</returns>
    public async ValueTask DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        if (NegotiatedVersion == NfsVersion.V4)
        {
            await DeleteV4Async(path, cancellationToken).ConfigureAwait(false);
            return;
        }

        (Nfs3Handle parent, string name) = await ResolveParentAsync(path, cancellationToken).ConfigureAwait(false);
        Nfs3LookupResult lookup = await Protocol3.LookupAsync(parent, name, cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(lookup.Status);

        Nfs3GetAttrResult attributes = await Protocol3
            .GetAttributesAsync(lookup.Ok.Handle, cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(attributes.Status);

        Nfs3WccResult result = attributes.Attributes.Type == NfsFileType.Directory
            ? await Protocol3.RemoveDirectoryAsync(parent, name, cancellationToken).ConfigureAwait(false)
            : await Protocol3.RemoveAsync(parent, name, cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _rpc.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<Nfs3Handle> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        Nfs3Handle current = _root3;
        foreach (string segment in SplitPath(path))
        {
            Nfs3LookupResult lookup = await Protocol3.LookupAsync(current, segment, cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(lookup.Status);
            current = lookup.Ok.Handle;
        }

        return current;
    }

    private async ValueTask<(Nfs3Handle Parent, string Name)> ResolveParentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string[] segments = SplitPath(path);
        if (segments.Length == 0)
        {
            throw new NfsException(NfsStatus.InvalidArgument);
        }

        Nfs3Handle parent = _root3;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            Nfs3LookupResult lookup = await Protocol3
                .LookupAsync(parent, segments[i], cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(lookup.Status);
            parent = lookup.Ok.Handle;
        }

        return (parent, segments[^1]);
    }

    private Nfs3Client Protocol3 => Protocol3OrNull ?? throw new NfsException(NfsStatus.InvalidArgument);

    private Nfs4Client Protocol4 => Protocol4OrNull ?? throw new NfsException(NfsStatus.InvalidArgument);

    private async ValueTask<NfsFileAttributes> StatV4Async(string path, CancellationToken cancellationToken)
    {
        Nfs4Handle handle = await ResolveV4Async(path, cancellationToken).ConfigureAwait(false);
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "stat",
            [new Nfs4PutFhOp { Handle = handle }, new Nfs4GetAttrOp { Request = Nfs4BasicAttributes }],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);

        Nfs4GetAttrResult attributes = (Nfs4GetAttrResult)result.Operations[^1];
        return ToAttributes(Nfs4FileAttributes.Decode(attributes.Attributes));
    }

    private async ValueTask<byte[]> ReadAllBytesV4Async(string path, CancellationToken cancellationToken)
    {
        Nfs4Handle file = await ResolveV4Async(path, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        ulong offset = 0;
        while (true)
        {
            Nfs4CompoundResult result = await Protocol4.CompoundAsync(
                "read",
                [new Nfs4PutFhOp { Handle = file }, new Nfs4ReadOp { Offset = offset, Count = ChunkSize }],
                cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(result.Status);

            Nfs4ReadResult read = (Nfs4ReadResult)result.Operations[^1];
            buffer.Write(read.Data);
            offset += (ulong)read.Data.Length;
            if (read.Eof || read.Data.Length == 0)
            {
                break;
            }
        }

        return buffer.ToArray();
    }

    private async ValueTask WriteAllBytesV4Async(
        string path,
        byte[] data,
        CancellationToken cancellationToken)
    {
        (Nfs4Handle parent, string name) = await ResolveParentV4Async(path, cancellationToken).ConfigureAwait(false);
        Nfs4OpenResult opened = await OpenV4Async(parent, name, cancellationToken).ConfigureAwait(false);
        Nfs4Handle file = await ResolveChildV4Async(parent, name, cancellationToken).ConfigureAwait(false);

        try
        {
            Nfs4FAttr truncate = new Nfs4FileAttributes { Size = 0 }.Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Size));
            Nfs4CompoundResult set = await Protocol4.CompoundAsync(
                "truncate",
                [new Nfs4PutFhOp { Handle = file }, new Nfs4SetAttrOp { StateId = opened.StateId, Attributes = truncate }],
                cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(set.Status);

            for (int written = 0; written < data.Length;)
            {
                int count = Math.Min(ChunkSize, data.Length - written);
                byte[] chunk = data.AsSpan(written, count).ToArray();
                Nfs4CompoundResult write = await Protocol4.CompoundAsync(
                    "write",
                    [
                        new Nfs4PutFhOp { Handle = file },
                        new Nfs4WriteOp
                        {
                            StateId = opened.StateId,
                            Offset = (ulong)written,
                            Stable = 2,
                            Data = chunk,
                        },
                    ],
                    cancellationToken).ConfigureAwait(false);
                Throw.IfFailed(write.Status);
                written += (int)((Nfs4WriteResult)write.Operations[^1]).Count;
            }
        }
        finally
        {
            await CloseV4Async(opened.StateId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<string>> ListV4Async(string path, CancellationToken cancellationToken)
    {
        Nfs4Handle directory = await ResolveV4Async(path, cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        ulong cookie = 0;
        byte[] verifier = new byte[Nfs4.VerifierSize];
        while (true)
        {
            Nfs4CompoundResult result = await Protocol4.CompoundAsync(
                "readdir",
                [
                    new Nfs4PutFhOp { Handle = directory },
                    new Nfs4ReadDirOp
                    {
                        Cookie = cookie,
                        CookieVerifier = verifier,
                        DirectoryCount = 8192,
                        MaxCount = 32768,
                        Request = Nfs4Bitmap.Empty,
                    },
                ],
                cancellationToken).ConfigureAwait(false);
            Throw.IfFailed(result.Status);

            Nfs4ReadDirResult readDir = (Nfs4ReadDirResult)result.Operations[^1];
            verifier = readDir.CookieVerifier;
            foreach (Nfs4DirEntry entry in readDir.Entries)
            {
                names.Add(entry.Name);
                cookie = entry.Cookie;
            }

            if (readDir.Eof || readDir.Entries.Length == 0)
            {
                break;
            }
        }

        return names;
    }

    private async ValueTask CreateDirectoryV4Async(string path, CancellationToken cancellationToken)
    {
        (Nfs4Handle parent, string name) = await ResolveParentV4Async(path, cancellationToken).ConfigureAwait(false);
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "mkdir",
            [
                new Nfs4PutFhOp { Handle = parent },
                new Nfs4CreateOp { Type = Nfs4CreateType.Directory, Name = name, Attributes = EmptyAttributes() },
            ],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
    }

    private async ValueTask DeleteV4Async(string path, CancellationToken cancellationToken)
    {
        (Nfs4Handle parent, string name) = await ResolveParentV4Async(path, cancellationToken).ConfigureAwait(false);
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "remove",
            [new Nfs4PutFhOp { Handle = parent }, new Nfs4RemoveOp { Name = name }],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
    }

    private async ValueTask<Nfs4Handle> ResolveV4Async(string path, CancellationToken cancellationToken)
    {
        Nfs4Handle current = _root4;
        foreach (string segment in SplitPath(path))
        {
            current = await ResolveChildV4Async(current, segment, cancellationToken).ConfigureAwait(false);
        }

        return current;
    }

    private async ValueTask<Nfs4Handle> ResolveChildV4Async(
        Nfs4Handle parent,
        string name,
        CancellationToken cancellationToken)
    {
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "lookup",
            [new Nfs4PutFhOp { Handle = parent }, new Nfs4LookupOp { Name = name }, new Nfs4GetFhOp()],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
        return ((Nfs4GetFhResult)result.Operations[^1]).Handle;
    }

    private async ValueTask<(Nfs4Handle Parent, string Name)> ResolveParentV4Async(
        string path,
        CancellationToken cancellationToken)
    {
        string[] segments = SplitPath(path);
        if (segments.Length == 0)
        {
            throw new NfsException(NfsStatus.InvalidArgument);
        }

        Nfs4Handle parent = _root4;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            parent = await ResolveChildV4Async(parent, segments[i], cancellationToken).ConfigureAwait(false);
        }

        return (parent, segments[^1]);
    }

    private async ValueTask<Nfs4OpenResult> OpenV4Async(
        Nfs4Handle parent,
        string name,
        CancellationToken cancellationToken)
    {
        ulong clientId = await GetClientIdV4Async(cancellationToken).ConfigureAwait(false);
        uint sequenceId = _openSequenceId++;
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "open",
            [
                new Nfs4PutFhOp { Handle = parent },
                new Nfs4OpenOp
                {
                    Seqid = sequenceId,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientId,
                    Owner = BitConverter.GetBytes(sequenceId),
                    OpenType = Nfs4OpenType.Create,
                    CreateMode = Nfs4CreateMode.Unchecked,
                    CreateAttributes = EmptyAttributes(),
                    Name = name,
                },
            ],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
        return (Nfs4OpenResult)result.Operations[^1];
    }

    private async ValueTask CloseV4Async(Nfs4StateId stateId, CancellationToken cancellationToken)
    {
        Nfs4CompoundResult result = await Protocol4.CompoundAsync(
            "close",
            [new Nfs4CloseOp { Seqid = _openSequenceId++, OpenStateId = stateId }],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(result.Status);
    }

    private async ValueTask<ulong> GetClientIdV4Async(CancellationToken cancellationToken)
    {
        if (_clientId != 0)
        {
            return _clientId;
        }

        Nfs4CompoundResult set = await Protocol4.CompoundAsync(
            "setclientid",
            [new Nfs4SetClientIdOp { Verifier = BitConverter.GetBytes(Environment.TickCount64), Id = Guid.NewGuid().ToByteArray() }],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(set.Status);
        var setClientId = (Nfs4SetClientIdResult)set.Operations[0];

        Nfs4CompoundResult confirm = await Protocol4.CompoundAsync(
            "confirm",
            [new Nfs4SetClientIdConfirmOp { ClientId = setClientId.ClientId, Confirm = setClientId.ConfirmVerifier }],
            cancellationToken).ConfigureAwait(false);
        Throw.IfFailed(confirm.Status);

        Nfs4CompoundResult reclaim = await Protocol4.CompoundAsync(
            "reclaim-complete",
            Nfs4.MinorVersion1,
            [new Nfs4ReclaimCompleteOp()],
            cancellationToken).ConfigureAwait(false);
        if (reclaim.Status != Nfs4Status.Ok && reclaim.Status != Nfs4Status.MinorVersionMismatch)
        {
            Throw.IfFailed(reclaim.Status);
        }

        _clientId = setClientId.ClientId;
        return _clientId;
    }

    private static Nfs4FAttr EmptyAttributes() => new() { Mask = Nfs4Bitmap.Empty, Values = [] };

    private static string[] SplitPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static NfsFileAttributes ToAttributes(Nfs3FileAttributes wire) => new()
    {
        Type = wire.Type,
        Mode = wire.Mode,
        LinkCount = wire.LinkCount,
        Uid = wire.Uid,
        Gid = wire.Gid,
        Size = wire.Size,
        Used = wire.Used,
        FileId = wire.FileId,
        AccessTime = new NfsTimestamp(wire.AccessTime.Seconds, wire.AccessTime.Nanoseconds),
        ModifyTime = new NfsTimestamp(wire.ModifyTime.Seconds, wire.ModifyTime.Nanoseconds),
        ChangeTime = new NfsTimestamp(wire.ChangeTime.Seconds, wire.ChangeTime.Nanoseconds),
    };

    private static NfsFileAttributes ToAttributes(Nfs4FileAttributes wire) => new()
    {
        Type = wire.Type is { } type ? (NfsFileType)type : NfsFileType.Regular,
        Mode = wire.Mode ?? 0,
        LinkCount = wire.NumLinks ?? 0,
        Size = wire.Size ?? 0,
        Used = wire.SpaceUsed ?? wire.Size ?? 0,
        FileId = wire.FileId ?? 0,
        AccessTime = ToTimestamp(wire.TimeAccess),
        ModifyTime = ToTimestamp(wire.TimeModify),
        ChangeTime = ToTimestamp(wire.TimeMetadata),
    };

    private static NfsTimestamp ToTimestamp(Nfs4Time? time) =>
        time is { } value ? new NfsTimestamp((uint)value.Seconds, value.Nanoseconds) : default;

    private static NfsStatus MapStatus(Nfs4Status status) => status switch
    {
        Nfs4Status.Ok => NfsStatus.Ok,
        Nfs4Status.NoEntry => NfsStatus.NoEntry,
        Nfs4Status.AlreadyExists => NfsStatus.AlreadyExists,
        Nfs4Status.NotDirectory => NfsStatus.NotDirectory,
        Nfs4Status.IsDirectory => NfsStatus.IsDirectory,
        Nfs4Status.BadHandle => NfsStatus.BadHandle,
        _ => NfsStatus.ServerFault,
    };

    private static class Throw
    {
        public static void IfFailed(NfsStatus status)
        {
            if (status != NfsStatus.Ok)
            {
                throw new NfsException(status);
            }
        }

        public static void IfFailed(Nfs4Status status)
        {
            if (status != Nfs4Status.Ok)
            {
                throw new NfsException(MapStatus(status), $"The NFSv4 operation failed with status {status}.");
            }
        }
    }
}
