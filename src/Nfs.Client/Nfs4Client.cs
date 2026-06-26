using System.Net;

using Nfs.Protocol.V4;
using Nfs.Rpc;

namespace Nfs.Client;

/// <summary>
/// A typed NFS version 4.0 client. It wraps an <see cref="RpcClient"/>, encoding COMPOUND requests
/// and decoding their replies for the NFS program (100003, version 4).
/// </summary>
public sealed class Nfs4Client : IAsyncDisposable
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;
    private readonly Dictionary<string, Nfs4PnfsLayout> _layoutCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Nfs4PnfsDevice> _deviceCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataServerConnection> _dataServers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _dataServerGate = new(1, 1);
    private bool _disposed;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Nfs4Client(IRpcClient rpc, OpaqueAuth credential = default)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _credential = credential;
    }

    /// <summary>Calls the NULL procedure, which does nothing but exercise the connection.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask NullAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Nfs4.Program,
            Nfs4.ProtocolVersion,
            (uint)Nfs4Procedure.Null,
            _credential,
            OpaqueAuth.None,
            default(Nfs.Xdr.XdrVoid),
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The NFS call was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }
    }

    /// <summary>Executes a COMPOUND request built from the given operations.</summary>
    /// <param name="tag">A human-readable request tag.</param>
    /// <param name="operations">The operations to execute, in order.</param>
    /// <returns>The decoded COMPOUND reply.</returns>
    public ValueTask<Nfs4CompoundResult> CompoundAsync(string tag, params Nfs4ArgOp[] operations) =>
        CompoundAsync(tag, (IEnumerable<Nfs4ArgOp>)operations, CancellationToken.None);

    /// <summary>Executes a COMPOUND request built from the given operations.</summary>
    /// <param name="tag">A human-readable request tag.</param>
    /// <param name="operations">The operations to execute, in order.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The decoded COMPOUND reply.</returns>
    public ValueTask<Nfs4CompoundResult> CompoundAsync(
        string tag,
        IEnumerable<Nfs4ArgOp> operations,
        CancellationToken cancellationToken = default) =>
        CompoundAsync(tag, Nfs4.MinorVersion0, operations, cancellationToken);

    /// <summary>Executes a COMPOUND request at a specific minor version.</summary>
    /// <param name="tag">A human-readable request tag.</param>
    /// <param name="minorVersion">The protocol minor version (0 for 4.0, 1 for 4.1).</param>
    /// <param name="operations">The operations to execute, in order.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The decoded COMPOUND reply.</returns>
    public async ValueTask<Nfs4CompoundResult> CompoundAsync(
        string tag,
        uint minorVersion,
        IEnumerable<Nfs4ArgOp> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var args = new Nfs4CompoundArgs { Tag = tag, MinorVersion = minorVersion };
        args.Operations.AddRange(operations);

        RpcReply reply = await _rpc.CallAsync(
            Nfs4.Program,
            Nfs4.ProtocolVersion,
            (uint)Nfs4Procedure.Compound,
            _credential,
            OpaqueAuth.None,
            args,
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The NFS call was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }

        return reply.DecodeResult<Nfs4CompoundResult>();
    }

    /// <summary>Gets and parses a pNFS files-layout device address.</summary>
    /// <param name="deviceId">The 16-byte device id.</param>
    /// <param name="session">The optional NFSv4.1 session sequencer.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The parsed files-layout device.</returns>
    public async ValueTask<Nfs4PnfsDevice> GetDeviceInfoAsync(
        ReadOnlyMemory<byte> deviceId,
        Nfs4PnfsSession? session = null,
        CancellationToken cancellationToken = default)
    {
        string key = DeviceKey(deviceId.Span);
        if (_deviceCache.TryGetValue(key, out Nfs4PnfsDevice? cached))
        {
            return cached;
        }

        Nfs4CompoundResult result = await CompoundPnfsAsync(
            "pnfs-getdeviceinfo",
            session,
            [new Nfs4GetDeviceInfoOp { DeviceId = deviceId.ToArray() }],
            cancellationToken).ConfigureAwait(false);
        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4GetDeviceInfoResult deviceInfo)
        {
            throw new RpcException($"GETDEVICEINFO failed with status {result.Status}.");
        }

        Nfs4PnfsDevice device = ParseDevice(deviceId, deviceInfo.DeviceAddress);
        _deviceCache[key] = device;
        return device;
    }

    /// <summary>Gets and parses a pNFS files layout for a file handle.</summary>
    /// <param name="handle">The file handle.</param>
    /// <param name="offset">The requested byte offset.</param>
    /// <param name="length">The requested byte length.</param>
    /// <param name="iomode">The requested I/O mode.</param>
    /// <param name="session">The optional NFSv4.1 session sequencer.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The parsed layout, or <see langword="null"/> when no layout is available.</returns>
    public async ValueTask<Nfs4PnfsLayout?> LayoutGetAsync(
        Nfs4Handle handle,
        ulong offset = 0,
        ulong length = ulong.MaxValue,
        Nfs4LayoutIomode iomode = Nfs4LayoutIomode.ReadWrite,
        Nfs4PnfsSession? session = null,
        CancellationToken cancellationToken = default)
    {
        Nfs4CompoundResult result = await CompoundPnfsAsync(
            "pnfs-layoutget",
            session,
            [
                new Nfs4PutFhOp { Handle = handle },
                new Nfs4LayoutGetOp
                {
                    Offset = offset,
                    Length = length,
                    Iomode = iomode,
                },
            ],
            cancellationToken).ConfigureAwait(false);

        if (result.Status == Nfs4Status.LayoutUnavailable || result.Status == Nfs4Status.NotSupported)
        {
            return null;
        }

        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4LayoutGetResult layoutGet)
        {
            throw new RpcException($"LAYOUTGET failed with status {result.Status}.");
        }

        if (layoutGet.Layouts.Length == 0)
        {
            return null;
        }

        Nfs4Layout layout = layoutGet.Layouts[0];
        if (layout.Content.LayoutType != Nfs4LayoutType.Files)
        {
            return null;
        }

        Nfs4FileLayout filesLayout = DecodeFilesLayout(layout.Content.Body);
        return new Nfs4PnfsLayout(layoutGet, layout, filesLayout);
    }

    /// <summary>Writes a byte range through a pNFS striped layout when available.</summary>
    /// <param name="handle">The metadata-server file handle.</param>
    /// <param name="offset">The starting byte offset.</param>
    /// <param name="data">The bytes to write.</param>
    /// <param name="session">The optional NFSv4.1 session sequencer.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    public async ValueTask WriteStripedAsync(
        Nfs4Handle handle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        Nfs4PnfsSession? session = null,
        CancellationToken cancellationToken = default)
    {
        if (data.IsEmpty)
        {
            return;
        }

        (Nfs4PnfsLayout Layout, Nfs4PnfsDevice Device)? fanout = await GetFanoutLayoutAsync(
            handle,
            offset,
            (ulong)data.Length,
            Nfs4LayoutIomode.ReadWrite,
            session,
            cancellationToken).ConfigureAwait(false);
        if (!fanout.HasValue)
        {
            await WritePlainAsync(handle, offset, data, cancellationToken).ConfigureAwait(false);
            return;
        }

        Nfs4PnfsLayout layout = fanout.Value.Layout;
        Nfs4PnfsDevice device = fanout.Value.Device;
        int position = 0;
        while (position < data.Length)
        {
            ulong currentOffset = offset + (ulong)position;
            int count = Math.Min(StripeRemaining(currentOffset, layout.FilesLayout.StripeUnit), data.Length - position);
            int dataServerIndex = DataServerIndex(layout.FilesLayout, device.FilesAddress, currentOffset);
            Nfs4Client dataServer = await GetDataServerClientAsync(
                device.DataServerEndpoints[dataServerIndex],
                cancellationToken).ConfigureAwait(false);

            await WritePlainAsync(
                dataServer,
                layout.FilesLayout.FileHandles[dataServerIndex],
                currentOffset,
                data.Slice(position, count),
                cancellationToken).ConfigureAwait(false);
            position += count;
        }

        await LayoutCommitAsync(
            handle,
            offset,
            (ulong)data.Length,
            offset + (ulong)data.Length,
            layout.Result.StateId,
            session,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads a byte range through a pNFS striped layout when available.</summary>
    /// <param name="handle">The metadata-server file handle.</param>
    /// <param name="offset">The starting byte offset.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <param name="session">The optional NFSv4.1 session sequencer.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The bytes read, reassembled in request order.</returns>
    public async ValueTask<byte[]> ReadStripedAsync(
        Nfs4Handle handle,
        ulong offset,
        int count,
        Nfs4PnfsSession? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        (Nfs4PnfsLayout Layout, Nfs4PnfsDevice Device)? fanout = await GetFanoutLayoutAsync(
            handle,
            offset,
            (ulong)count,
            Nfs4LayoutIomode.Read,
            session,
            cancellationToken).ConfigureAwait(false);
        if (!fanout.HasValue)
        {
            return await ReadPlainAsync(handle, offset, count, cancellationToken).ConfigureAwait(false);
        }

        Nfs4PnfsLayout layout = fanout.Value.Layout;
        Nfs4PnfsDevice device = fanout.Value.Device;
        byte[] result = new byte[count];
        int position = 0;
        while (position < count)
        {
            ulong currentOffset = offset + (ulong)position;
            int requested = Math.Min(StripeRemaining(currentOffset, layout.FilesLayout.StripeUnit), count - position);
            int dataServerIndex = DataServerIndex(layout.FilesLayout, device.FilesAddress, currentOffset);
            Nfs4Client dataServer = await GetDataServerClientAsync(
                device.DataServerEndpoints[dataServerIndex],
                cancellationToken).ConfigureAwait(false);

            byte[] chunk = await ReadPlainAsync(
                dataServer,
                layout.FilesLayout.FileHandles[dataServerIndex],
                currentOffset,
                requested,
                cancellationToken).ConfigureAwait(false);
            chunk.CopyTo(result.AsSpan(position));
            position += chunk.Length;
            if (chunk.Length < requested)
            {
                return result.AsSpan(0, position).ToArray();
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (DataServerConnection dataServer in _dataServers.Values)
        {
            await dataServer.Client.DisposeAsync().ConfigureAwait(false);
            await dataServer.Rpc.DisposeAsync().ConfigureAwait(false);
        }

        _dataServerGate.Dispose();
    }

    /// <summary>Gets the export root handle (PUTROOTFH + GETFH).</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The root handle.</returns>
    /// <exception cref="RpcException">The server did not return a handle.</exception>
    public async ValueTask<Nfs4Handle> GetRootHandleAsync(CancellationToken cancellationToken = default)
    {
        Nfs4CompoundResult result = await CompoundAsync(
            "getroot",
            [new Nfs4PutRootFhOp(), new Nfs4GetFhOp()],
            cancellationToken).ConfigureAwait(false);

        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4GetFhResult { Handle: var handle })
        {
            throw new RpcException($"PUTROOTFH/GETFH failed with status {result.Status}.");
        }

        return handle;
    }

    /// <summary>Issues OP_OPEN_DOWNGRADE for an existing open state identifier.</summary>
    public async ValueTask<Nfs4StateIdResult> OpenDowngradeAsync(
        Nfs4StateId stateId,
        uint seqid,
        uint shareAccess,
        uint shareDeny = 0,
        CancellationToken cancellationToken = default)
    {
        Nfs4CompoundResult result = await CompoundAsync(
            "open-downgrade",
            [
                new Nfs4OpenDowngradeOp
                {
                    OpenStateId = stateId,
                    Seqid = seqid,
                    ShareAccess = shareAccess,
                    ShareDeny = shareDeny,
                },
            ],
            cancellationToken).ConfigureAwait(false);

        return (Nfs4StateIdResult)result.Operations[0];
    }

    /// <summary>Issues OP_COMMIT against the current file handle selected by <paramref name="prefix"/>.</summary>
    public async ValueTask<Nfs4CommitResult> CommitAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "commit",
            [.. prefix, new Nfs4CommitOp { Offset = offset, Count = count }],
            cancellationToken).ConfigureAwait(false);

        return (Nfs4CommitResult)result.Operations[^1];
    }

    /// <summary>Issues OP_LINK using the saved file handle as source and current file handle as target directory.</summary>
    public async ValueTask<Nfs4LinkResult> LinkAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "link",
            [.. prefix, new Nfs4LinkOp { NewName = newName }],
            cancellationToken).ConfigureAwait(false);

        return (Nfs4LinkResult)result.Operations[^1];
    }

    /// <summary>Issues OP_OPENATTR against the current file handle selected by <paramref name="prefix"/>.</summary>
    public async ValueTask<Nfs4Status> OpenAttrAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        bool createDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "openattr",
            [.. prefix, new Nfs4OpenAttrOp { CreateDirectory = createDirectory }],
            cancellationToken).ConfigureAwait(false);

        return result.Status;
    }

    /// <summary>Issues OP_FREE_STATEID for a lock state identifier.</summary>
    public async ValueTask<Nfs4Status> FreeStateIdAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        Nfs4StateId stateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "free-stateid",
            Nfs4.MinorVersion1,
            [.. prefix, new Nfs4FreeStateIdOp { StateId = stateId }],
            cancellationToken).ConfigureAwait(false);

        return result.Status;
    }

    /// <summary>Issues OP_TEST_STATEID for one or more state identifiers.</summary>
    public async ValueTask<Nfs4TestStateIdResult> TestStateIdAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        IEnumerable<Nfs4StateId> stateIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(stateIds);
        var op = new Nfs4TestStateIdOp();
        op.StateIds.AddRange(stateIds);
        Nfs4CompoundResult result = await CompoundAsync(
            "test-stateid",
            Nfs4.MinorVersion1,
            [.. prefix, op],
            cancellationToken).ConfigureAwait(false);

        return (Nfs4TestStateIdResult)result.Operations[^1];
    }

    /// <summary>Issues OP_BIND_CONN_TO_SESSION for the current RPC connection.</summary>
    public async ValueTask<Nfs4BindConnToSessionResult> BindConnectionToSessionAsync(
        byte[] sessionId,
        Nfs4ChannelDirectionFromClient direction = Nfs4ChannelDirectionFromClient.Fore,
        bool useConnectionInRdmaMode = false,
        CancellationToken cancellationToken = default)
    {
        Nfs4CompoundResult result = await CompoundAsync(
            "bind-conn-to-session",
            Nfs4.MinorVersion1,
            [
                new Nfs4BindConnToSessionOp
                {
                    SessionId = sessionId,
                    Direction = direction,
                    UseConnectionInRdmaMode = useConnectionInRdmaMode,
                },
            ],
            cancellationToken).ConfigureAwait(false);

        return (Nfs4BindConnToSessionResult)result.Operations[0];
    }

    /// <summary>Issues OP_BACKCHANNEL_CTL for a sequenced session.</summary>
    public async ValueTask<Nfs4Status> BackchannelCtlAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        uint callbackProgram,
        IEnumerable<int>? callbackSecurityFlavors = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var op = new Nfs4BackchannelCtlOp { CallbackProgram = callbackProgram };
        op.CallbackSecurityFlavors.Clear();
        op.CallbackSecurityFlavors.AddRange(callbackSecurityFlavors ?? [0]);
        Nfs4CompoundResult result = await CompoundAsync(
            "backchannel-ctl",
            Nfs4.MinorVersion1,
            [.. prefix, op],
            cancellationToken).ConfigureAwait(false);

        return result.Status;
    }

    /// <summary>Issues OP_VERIFY against the current file handle selected by <paramref name="prefix"/>.</summary>
    public async ValueTask<Nfs4Status> VerifyAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        Nfs4FAttr attributes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "verify",
            [.. prefix, new Nfs4VerifyOp { Attributes = attributes }],
            cancellationToken).ConfigureAwait(false);

        return result.Status;
    }

    /// <summary>Issues OP_NVERIFY against the current file handle selected by <paramref name="prefix"/>.</summary>
    public async ValueTask<Nfs4Status> NverifyAsync(
        IEnumerable<Nfs4ArgOp> prefix,
        Nfs4FAttr attributes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Nfs4CompoundResult result = await CompoundAsync(
            "nverify",
            [.. prefix, new Nfs4NverifyOp { Attributes = attributes }],
            cancellationToken).ConfigureAwait(false);

        return result.Status;
    }

    private async ValueTask<(Nfs4PnfsLayout Layout, Nfs4PnfsDevice Device)?> GetFanoutLayoutAsync(
        Nfs4Handle handle,
        ulong offset,
        ulong length,
        Nfs4LayoutIomode iomode,
        Nfs4PnfsSession? session,
        CancellationToken cancellationToken)
    {
        string key = HandleKey(handle);
        if (!_layoutCache.TryGetValue(key, out Nfs4PnfsLayout? layout))
        {
            layout = await LayoutGetAsync(handle, offset, length, iomode, session, cancellationToken).ConfigureAwait(false);
            if (layout is null)
            {
                return null;
            }

            _layoutCache[key] = layout;
        }

        Nfs4PnfsDevice device = await GetDeviceInfoAsync(layout.FilesLayout.DeviceId, session, cancellationToken)
            .ConfigureAwait(false);
        if (!CanFanOut(layout, device))
        {
            return null;
        }

        return (layout, device);
    }

    private static bool CanFanOut(Nfs4PnfsLayout layout, Nfs4PnfsDevice device)
    {
        Nfs4FileLayout filesLayout = layout.FilesLayout;
        Nfs4FileLayoutDataServerAddress filesAddress = device.FilesAddress;
        return layout.IsDense
            && filesLayout.StripeUnit > 0
            && filesAddress.StripeIndices.Length > 1
            && filesLayout.FileHandles.Length > 1
            && device.DataServerEndpoints.Count > 1
            && filesAddress.StripeIndices.All(index =>
                index < filesLayout.FileHandles.Length && index < device.DataServerEndpoints.Count);
    }

    private async ValueTask<Nfs4CompoundResult> CompoundPnfsAsync(
        string tag,
        Nfs4PnfsSession? session,
        IReadOnlyList<Nfs4ArgOp> operations,
        CancellationToken cancellationToken)
    {
        Nfs4ArgOp[] compoundOperations = session is null
            ? [.. operations]
            : [session.NextSequence(), .. operations];
        return await CompoundAsync(tag, Nfs4.MinorVersion1, compoundOperations, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask LayoutCommitAsync(
        Nfs4Handle handle,
        ulong offset,
        ulong length,
        ulong newOffset,
        Nfs4StateId stateId,
        Nfs4PnfsSession? session,
        CancellationToken cancellationToken)
    {
        Nfs4CompoundResult result = await CompoundPnfsAsync(
            "pnfs-layoutcommit",
            session,
            [
                new Nfs4PutFhOp { Handle = handle },
                new Nfs4LayoutCommitOp
                {
                    Offset = offset,
                    Length = length,
                    StateId = stateId,
                    NewOffset = newOffset,
                },
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.Status != Nfs4Status.Ok)
        {
            throw new RpcException($"LAYOUTCOMMIT failed with status {result.Status}.");
        }
    }

    private async ValueTask<Nfs4Client> GetDataServerClientAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        string key = endpoint.ToString();
        await _dataServerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_dataServers.TryGetValue(key, out DataServerConnection? cached))
            {
                return cached.Client;
            }

            RpcClient rpc = await RpcClient.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var client = new Nfs4Client(rpc, _credential);
            _dataServers[key] = new DataServerConnection(rpc, client);
            return client;
        }
        finally
        {
            _dataServerGate.Release();
        }
    }

    private async ValueTask WritePlainAsync(
        Nfs4Handle handle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken) =>
        await WritePlainAsync(this, handle, offset, data, cancellationToken).ConfigureAwait(false);

    private static async ValueTask WritePlainAsync(
        Nfs4Client client,
        Nfs4Handle handle,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        Nfs4CompoundResult result = await client.CompoundAsync(
            "write",
            [
                new Nfs4PutFhOp { Handle = handle },
                new Nfs4WriteOp
                {
                    Offset = offset,
                    Data = data.ToArray(),
                    Stable = 2,
                },
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4WriteResult write)
        {
            throw new RpcException($"WRITE failed with status {result.Status}.");
        }

        if (write.Count != data.Length)
        {
            throw new RpcException($"WRITE accepted {write.Count} of {data.Length} bytes.");
        }
    }

    private async ValueTask<byte[]> ReadPlainAsync(
        Nfs4Handle handle,
        ulong offset,
        int count,
        CancellationToken cancellationToken) =>
        await ReadPlainAsync(this, handle, offset, count, cancellationToken).ConfigureAwait(false);

    private static async ValueTask<byte[]> ReadPlainAsync(
        Nfs4Client client,
        Nfs4Handle handle,
        ulong offset,
        int count,
        CancellationToken cancellationToken)
    {
        Nfs4CompoundResult result = await client.CompoundAsync(
            "read",
            [
                new Nfs4PutFhOp { Handle = handle },
                new Nfs4ReadOp { Offset = offset, Count = checked((uint)count) },
            ],
            cancellationToken).ConfigureAwait(false);
        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4ReadResult read)
        {
            throw new RpcException($"READ failed with status {result.Status}.");
        }

        return read.Data;
    }

    private static Nfs4PnfsDevice ParseDevice(ReadOnlyMemory<byte> deviceId, Nfs4DeviceAddress address)
    {
        if (address.LayoutType != Nfs4LayoutType.Files)
        {
            throw new RpcException($"Unsupported pNFS device layout type {address.LayoutType}.");
        }

        Nfs4FileLayoutDataServerAddress filesAddress = DecodeFilesAddress(address.Body);
        IPEndPoint[] endpoints = new IPEndPoint[filesAddress.MultipathDataServers.Length];
        for (int i = 0; i < endpoints.Length; i++)
        {
            endpoints[i] = ParseDataServerEndpoint(filesAddress.MultipathDataServers[i]);
        }

        return new Nfs4PnfsDevice(deviceId.ToArray(), address, filesAddress, endpoints);
    }

    private static IPEndPoint ParseDataServerEndpoint(Nfs4NetAddress[] paths)
    {
        foreach (Nfs4NetAddress path in paths)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(path.NetId, "tcp"))
            {
                continue;
            }

            return ParseUniversalAddress(path.Uaddr);
        }

        throw new RpcException("The pNFS device did not include a TCP data-server address.");
    }

    private static IPEndPoint ParseUniversalAddress(string uaddr)
    {
        string[] parts = uaddr.Split('.');
        if (parts.Length != 6
            || !byte.TryParse(parts[0], out byte a)
            || !byte.TryParse(parts[1], out byte b)
            || !byte.TryParse(parts[2], out byte c)
            || !byte.TryParse(parts[3], out byte d)
            || !byte.TryParse(parts[4], out byte p1)
            || !byte.TryParse(parts[5], out byte p2))
        {
            throw new RpcException($"Unsupported pNFS universal address '{uaddr}'.");
        }

        var address = new IPAddress([a, b, c, d]);
        int port = (p1 << 8) | p2;
        return new IPEndPoint(address, port);
    }

    private static int DataServerIndex(
        Nfs4FileLayout layout,
        Nfs4FileLayoutDataServerAddress address,
        ulong offset)
    {
        ulong stripe = offset / layout.StripeUnit;
        uint stripeSlot = (uint)((layout.FirstStripeIndex + stripe) % (ulong)address.StripeIndices.Length);
        return checked((int)address.StripeIndices[stripeSlot]);
    }

    private static int StripeRemaining(ulong offset, uint stripeUnit)
    {
        int unit = checked((int)stripeUnit);
        return unit - checked((int)(offset % stripeUnit));
    }

    private static Nfs4FileLayout DecodeFilesLayout(ReadOnlyMemory<byte> body) => Nfs4FileLayout.Decode(body);

    private static Nfs4FileLayoutDataServerAddress DecodeFilesAddress(ReadOnlyMemory<byte> body) =>
        Nfs4FileLayoutDataServerAddress.Decode(body);

    private static string DeviceKey(ReadOnlySpan<byte> deviceId) => Convert.ToHexString(deviceId);

    private static string HandleKey(Nfs4Handle handle) => Convert.ToHexString(handle.Data);

    private sealed class DataServerConnection(RpcClient rpc, Nfs4Client client)
    {
        public RpcClient Rpc { get; } = rpc;

        public Nfs4Client Client { get; } = client;
    }
}
