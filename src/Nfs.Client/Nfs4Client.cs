using Nfs.Protocol.V4;
using Nfs.Rpc;

namespace Nfs.Client;

/// <summary>
/// A typed NFS version 4.0 client. It wraps an <see cref="RpcClient"/>, encoding COMPOUND requests
/// and decoding their replies for the NFS program (100003, version 4).
/// </summary>
public sealed class Nfs4Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;

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
}
