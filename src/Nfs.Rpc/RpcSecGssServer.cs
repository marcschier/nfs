using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Nfs.Rpc;

/// <summary>Server-side RPCSEC_GSS context store and call authenticator.</summary>
public sealed class RpcSecGssServer
{
    private readonly ConcurrentDictionary<string, IGssContext> _contexts = new(StringComparer.Ordinal);
    private readonly IGssMechanism _mechanism;
    private int _nextHandle;

    /// <summary>Initializes a new server-side RPCSEC_GSS context store.</summary>
    /// <param name="mechanism">The GSS mechanism used to accept context tokens.</param>
    /// <param name="sequenceWindow">The sequence window reported during context establishment.</param>
    public RpcSecGssServer(IGssMechanism mechanism, uint sequenceWindow = 128)
    {
        ArgumentNullException.ThrowIfNull(mechanism);
        _mechanism = mechanism;
        SequenceWindow = sequenceWindow;
    }

    /// <summary>Gets the sequence window reported to clients during context establishment.</summary>
    public uint SequenceWindow { get; }

    internal RpcSecGssServerResult ProcessInit(ReadOnlyMemory<byte> arguments)
    {
        byte[] inputToken = RpcSecGssWire.DecodeInitArgument(arguments);
        IGssContext context = _mechanism.CreateServerContext();
        GssTokenResult step = context.Accept(inputToken);
        byte[] handle = CreateHandle();
        if (context.IsEstablished)
        {
            _contexts[HandleKey(handle)] = context;
        }

        byte[] result = RpcSecGssWire.EncodeInitResult(
            handle, step.MajorStatus, step.MinorStatus, SequenceWindow, step.OutputToken.Span);
        return RpcSecGssServerResult.Handshake(result);
    }

    internal RpcSecGssServerResult ProcessData(
        RpcCallHeader header,
        ReadOnlyMemory<byte> arguments)
    {
        RpcSecGssCredential credential = RpcSecGssWire.DecodeCredential(header.Credential);
        byte[] handle = credential.Handle.ToArray();
        if (!_contexts.TryGetValue(HandleKey(handle), out IGssContext? context))
        {
            throw new RpcException("RPCSEC_GSS context handle was not found.");
        }

        byte[] headerPrefix = RpcMessageCodec.EncodeCallHeaderPrefix(header);
        if (!context.VerifyMic(headerPrefix, header.Verifier.Body.Span))
        {
            throw new RpcException("RPCSEC_GSS call verifier MIC was invalid.");
        }

        byte[] unprotected = RpcSecGssWire.UnprotectData(
            context, credential.Service, credential.SequenceNumber, arguments);
        var callContext = new RpcSecGssCallContext(
            handle, credential.SequenceNumber, credential.Service, context);
        return RpcSecGssServerResult.Data(unprotected, callContext);
    }

    internal static RpcReplyPayload ProtectReply(RpcReplyPayload payload, RpcSecGssCallContext context)
    {
        if (payload.Status != AcceptStatus.Success)
        {
            return payload;
        }

        byte[] protectedResult = RpcSecGssWire.ProtectData(
            context.Context, context.Service, context.SequenceNumber, payload.Result.Span);
        return RpcReplyPayload.Success(protectedResult);
    }

    internal static OpaqueAuth CreateReplyVerifier(RpcSecGssCallContext context)
    {
        byte[] sequenceBytes = RpcSecGssWire.EncodeSequenceNumber(context.SequenceNumber);
        byte[] mic = context.Context.GetMic(sequenceBytes);
        return new OpaqueAuth(AuthFlavor.RpcSecGss, mic);
    }

    private byte[] CreateHandle()
    {
        byte[] handle = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(handle, (ulong)Interlocked.Increment(ref _nextHandle));
        return handle;
    }

    private static string HandleKey(ReadOnlySpan<byte> handle) => Convert.ToHexString(handle);
}

internal readonly record struct RpcSecGssServerResult(
    bool IsHandshake,
    ReadOnlyMemory<byte> Payload,
    RpcSecGssCallContext? Context)
{
    public static RpcSecGssServerResult Handshake(ReadOnlyMemory<byte> result) =>
        new(true, result, null);

    public static RpcSecGssServerResult Data(
        ReadOnlyMemory<byte> arguments,
        RpcSecGssCallContext context) =>
        new(false, arguments, context);
}
