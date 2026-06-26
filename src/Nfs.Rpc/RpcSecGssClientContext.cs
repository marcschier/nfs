using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>Client-side RPCSEC_GSS context state used by <see cref="RpcClient"/>.</summary>
public sealed class RpcSecGssClientContext
{
    private readonly IGssContext _context;
    private uint _nextSequenceNumber = 1;

    private RpcSecGssClientContext(
        IGssContext context,
        ReadOnlyMemory<byte> handle,
        uint sequenceWindow)
    {
        _context = context;
        Handle = handle;
        SequenceWindow = sequenceWindow;
    }

    /// <summary>Gets the server-assigned RPCSEC_GSS context handle.</summary>
    public ReadOnlyMemory<byte> Handle { get; }

    /// <summary>Gets the sequence window reported by the server.</summary>
    public uint SequenceWindow { get; }

    /// <summary>Establishes an RPCSEC_GSS context by calling the remote program's NULL procedure.</summary>
    /// <param name="client">The connected RPC client.</param>
    /// <param name="program">The remote program number.</param>
    /// <param name="version">The remote program version.</param>
    /// <param name="mechanism">The GSS mechanism to use.</param>
    /// <param name="targetName">The peer service principal or mechanism-specific target name.</param>
    /// <param name="cancellationToken">A token to cancel the handshake call.</param>
    /// <returns>The established RPCSEC_GSS client context.</returns>
    public static async ValueTask<RpcSecGssClientContext> EstablishAsync(
        RpcClient client,
        uint program,
        uint version,
        IGssMechanism mechanism,
        string? targetName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mechanism);

        IGssContext context = mechanism.CreateClientContext(targetName);
        GssTokenResult step = context.Init(ReadOnlySpan<byte>.Empty);
        RpcSecGssProcedure procedure = RpcSecGssProcedure.Init;
        ReadOnlyMemory<byte> handle = ReadOnlyMemory<byte>.Empty;
        uint sequenceWindow = 0;
        bool serverEstablished = false;
        while (!serverEstablished || !context.IsEstablished || step.OutputToken.Length != 0)
        {
            OpaqueAuth credential = RpcSecGssWire.CreateCredential(
                procedure, sequenceNumber: 0, RpcSecGssService.None, handle.Span);
            var argument = new RpcSecGssInitArgument(step.OutputToken);
            RpcReply reply = await client.CallAsync(
                    program, version, procedure: 0, credential, OpaqueAuth.None, argument, cancellationToken)
                .ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                throw new RpcException($"RPCSEC_GSS INIT failed with status {reply.Header.Status}/{reply.Header.Accept}.");
            }

            RpcSecGssInitResult initResult = RpcSecGssWire.DecodeInitResult(reply.Result);
            handle = initResult.Handle;
            sequenceWindow = initResult.SequenceWindow;
            serverEstablished = initResult.MajorStatus == GssMajorStatus.Complete;
            if (initResult.MajorStatus is not (GssMajorStatus.Complete or GssMajorStatus.ContinueNeeded))
            {
                throw new RpcException($"RPCSEC_GSS INIT failed with GSS status {initResult.MajorStatus}.");
            }

            if (!context.IsEstablished || !initResult.OutputToken.IsEmpty)
            {
                step = context.Init(initResult.OutputToken.Span);
            }
            else
            {
                step = new GssTokenResult(ReadOnlyMemory<byte>.Empty, GssMajorStatus.Complete, 0);
            }

            procedure = RpcSecGssProcedure.ContinueInit;
        }

        if (!context.IsEstablished)
        {
            throw new RpcException("RPCSEC_GSS client context was not established.");
        }

        return new RpcSecGssClientContext(context, handle, sequenceWindow);
    }

    internal RpcSecGssClientCall CreateCall<TArgs>(
        uint xid,
        uint program,
        uint version,
        uint procedure,
        RpcSecGssService service,
        TArgs arguments)
        where TArgs : IXdrSerializable<TArgs>
    {
        uint sequenceNumber = _nextSequenceNumber++;
        OpaqueAuth credential = RpcSecGssWire.CreateCredential(
            RpcSecGssProcedure.Data, sequenceNumber, service, Handle.Span);
        var header = new RpcCallHeader(
            xid, program, version, procedure, credential, OpaqueAuth.None);
        byte[] headerPrefix = RpcMessageCodec.EncodeCallHeaderPrefix(header);
        OpaqueAuth verifier = new(AuthFlavor.RpcSecGss, _context.GetMic(headerPrefix));
        byte[] plainArguments = RpcSecGssWire.EncodePlainArguments(arguments);
        byte[] protectedArguments = RpcSecGssWire.ProtectData(
            _context, service, sequenceNumber, plainArguments);
        return new RpcSecGssClientCall(
            credential, verifier, new RpcSecGssRawBody(protectedArguments), sequenceNumber, service);
    }

    internal RpcReply DecodeReply(RpcReply reply, uint sequenceNumber, RpcSecGssService service)
    {
        byte[] sequenceBytes = RpcSecGssWire.EncodeSequenceNumber(sequenceNumber);
        if (!reply.Header.Verifier.Body.IsEmpty
            && !_context.VerifyMic(sequenceBytes, reply.Header.Verifier.Body.Span))
        {
            throw new RpcException("RPCSEC_GSS reply verifier MIC was invalid.");
        }

        if (!reply.IsSuccess)
        {
            return reply;
        }

        byte[] result = RpcSecGssWire.UnprotectData(_context, service, sequenceNumber, reply.Result);
        return new RpcReply(reply.Header, result);
    }
}

internal readonly record struct RpcSecGssClientCall(
    OpaqueAuth Credential,
    OpaqueAuth Verifier,
    RpcSecGssRawBody Arguments,
    uint SequenceNumber,
    RpcSecGssService Service);
