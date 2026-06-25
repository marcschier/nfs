using System.Buffers;

using Nfs.Abstractions;
using Nfs.Mount;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the MOUNT version 3 program (100005). It maps export
/// paths to <see cref="INfsFileSystem"/> backends and answers MNT with the export's root handle.
/// </summary>
public sealed class Nfs3MountProgram : IRpcProgram
{
    private static readonly int[] DefaultAuthFlavors = [1, 0]; // AUTH_SYS, AUTH_NONE

    private readonly Dictionary<string, INfsFileSystem> _exports = new(StringComparer.Ordinal);
    private readonly List<Mount3MountEntry> _mounts = [];
    private readonly object _gate = new();

    /// <summary>Creates a MOUNT server exposing a single export.</summary>
    /// <param name="exportPath">The export path (for example <c>/</c>).</param>
    /// <param name="fileSystem">The backend serving the export.</param>
    public Nfs3MountProgram(string exportPath, INfsFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(exportPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _exports[Normalize(exportPath)] = fileSystem;
    }

    /// <inheritdoc/>
    public uint Program => Mount3.Program;

    /// <summary>Adds an export.</summary>
    /// <param name="exportPath">The export path.</param>
    /// <param name="fileSystem">The backend serving the export.</param>
    public void AddExport(string exportPath, INfsFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(exportPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        lock (_gate)
        {
            _exports[Normalize(exportPath)] = fileSystem;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Mount3.ProtocolVersion)
        {
            return RpcReplyPayload.ProgramMismatch(Mount3.ProtocolVersion, Mount3.ProtocolVersion);
        }

        return (Mount3Procedure)request.Procedure switch
        {
            Mount3Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Mount3Procedure.Mount => await MountAsync(request, arguments, cancellationToken).ConfigureAwait(false),
            Mount3Procedure.Dump => Dump(),
            Mount3Procedure.Unmount => Unmount(request, arguments),
            Mount3Procedure.Export => Export(),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };
    }

    private async ValueTask<RpcReplyPayload> MountAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Mount3MountArgs args = DecodeMount(arguments);
        string path = Normalize(args.Path);

        if (!_exports.TryGetValue(path, out INfsFileSystem? fileSystem))
        {
            return Encode(Mount3MountResult.Failure(Mount3Status.NoEntry));
        }

        Mount3MountResult result;
        try
        {
            NfsFileHandle root = await fileSystem.GetRootHandleAsync(cancellationToken).ConfigureAwait(false);
            result = Mount3MountResult.Success(new Mount3MountResultOk
            {
                Handle = root.ToArray(),
                AuthFlavors = DefaultAuthFlavors,
            });
            RecordMount(GetHostname(request.Credential), path);
        }
        catch (NfsException ex)
        {
            result = Mount3MountResult.Failure(MapStatus(ex.Status));
        }

        return Encode(result);
    }

    private RpcReplyPayload Dump()
    {
        lock (_gate)
        {
            return Encode(new Mount3MountList { Mounts = [.. _mounts] });
        }
    }

    private RpcReplyPayload Unmount(RpcCallInfo request, ReadOnlyMemory<byte> arguments)
    {
        Mount3MountArgs args = DecodeMount(arguments);
        string hostname = GetHostname(request.Credential);
        string path = Normalize(args.Path);

        lock (_gate)
        {
            _mounts.RemoveAll(mount =>
                string.Equals(mount.Hostname, hostname, StringComparison.Ordinal) &&
                string.Equals(mount.Directory, path, StringComparison.Ordinal));
        }

        return RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty);
    }

    private RpcReplyPayload Export()
    {
        Mount3ExportEntry[] exports;
        lock (_gate)
        {
            exports = [.. _exports.Keys.Select(path => new Mount3ExportEntry(path, []))];
        }

        return Encode(new Mount3ExportList { Exports = exports });
    }

    private void RecordMount(string hostname, string path)
    {
        lock (_gate)
        {
            if (!_mounts.Any(mount =>
                string.Equals(mount.Hostname, hostname, StringComparison.Ordinal) &&
                string.Equals(mount.Directory, path, StringComparison.Ordinal)))
            {
                _mounts.Add(new Mount3MountEntry(hostname, path));
            }
        }
    }

    private static Mount3MountArgs DecodeMount(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Mount3MountArgs.ReadFrom(ref reader);
    }

    private static string GetHostname(OpaqueAuth credential)
    {
        if (credential.Flavor != AuthFlavor.Sys)
        {
            return "unknown";
        }

        try
        {
            var reader = new XdrReader(credential.Body.Span);
            _ = reader.ReadUInt32();
            string hostname = reader.ReadString(Mount3.MaxNameLength);
            return string.IsNullOrWhiteSpace(hostname) ? "unknown" : hostname;
        }
        catch (XdrException)
        {
            return "unknown";
        }
    }

    private static RpcReplyPayload Encode<T>(T result)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }

    private static string Normalize(string path)
    {
        string trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static Mount3Status MapStatus(NfsStatus status) => status switch
    {
        NfsStatus.NoEntry => Mount3Status.NoEntry,
        NfsStatus.AccessDenied => Mount3Status.AccessDenied,
        NfsStatus.NotDirectory => Mount3Status.NotDirectory,
        NfsStatus.NotOwner => Mount3Status.NotOwner,
        _ => Mount3Status.ServerFault,
    };
}
