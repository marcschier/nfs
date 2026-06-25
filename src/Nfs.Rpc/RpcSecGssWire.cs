using System.Buffers;

using Nfs.Xdr;

namespace Nfs.Rpc;

internal static class RpcSecGssWire
{
    public const uint Version = 1;
    public const int MaxHandleLength = 400;
    public const int MaxTokenLength = 1024 * 1024;
    public const int MaxProtectedBodyLength = 16 * 1024 * 1024;

    public static OpaqueAuth CreateCredential(
        RpcSecGssProcedure procedure,
        uint sequenceNumber,
        RpcSecGssService service,
        ReadOnlySpan<byte> handle)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(Version);
        writer.WriteUInt32((uint)procedure);
        writer.WriteUInt32(sequenceNumber);
        writer.WriteUInt32((uint)service);
        writer.WriteOpaqueVariable(handle);
        return new OpaqueAuth(AuthFlavor.RpcSecGss, buffer.WrittenSpan.ToArray());
    }

    public static RpcSecGssCredential DecodeCredential(OpaqueAuth credential)
    {
        if (credential.Flavor != AuthFlavor.RpcSecGss)
        {
            throw new RpcException($"Expected RPCSEC_GSS credentials but found {credential.Flavor}.");
        }

        var reader = new XdrReader(credential.Body.Span);
        uint version = reader.ReadUInt32();
        if (version != Version)
        {
            throw new RpcException($"Unsupported RPCSEC_GSS credential version {version}.");
        }

        var procedure = (RpcSecGssProcedure)reader.ReadUInt32();
        uint sequenceNumber = reader.ReadUInt32();
        var service = (RpcSecGssService)reader.ReadUInt32();
        byte[] handle = reader.ReadOpaqueVariable(MaxHandleLength).ToArray();
        return new RpcSecGssCredential(procedure, sequenceNumber, service, handle);
    }

    public static byte[] EncodeInitArgument(ReadOnlySpan<byte> token)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueVariable(token);
        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] DecodeInitArgument(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return reader.ReadOpaqueVariable(MaxTokenLength).ToArray();
    }

    public static byte[] EncodeInitResult(
        ReadOnlySpan<byte> handle,
        GssMajorStatus major,
        uint minor,
        uint sequenceWindow,
        ReadOnlySpan<byte> token)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueVariable(handle);
        writer.WriteUInt32((uint)major);
        writer.WriteUInt32(minor);
        writer.WriteUInt32(sequenceWindow);
        writer.WriteOpaqueVariable(token);
        return buffer.WrittenSpan.ToArray();
    }

    public static RpcSecGssInitResult DecodeInitResult(ReadOnlyMemory<byte> result)
    {
        var reader = new XdrReader(result.Span);
        byte[] handle = reader.ReadOpaqueVariable(MaxHandleLength).ToArray();
        var major = (GssMajorStatus)reader.ReadUInt32();
        uint minor = reader.ReadUInt32();
        uint sequenceWindow = reader.ReadUInt32();
        byte[] token = reader.ReadOpaqueVariable(MaxTokenLength).ToArray();
        return new RpcSecGssInitResult(handle, major, minor, sequenceWindow, token);
    }

    public static byte[] EncodePlainArguments<TArgs>(TArgs arguments)
        where TArgs : IXdrSerializable<TArgs>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        arguments.WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] ProtectData(
        IGssContext context,
        RpcSecGssService service,
        uint sequenceNumber,
        ReadOnlySpan<byte> payload)
    {
        if (service == RpcSecGssService.None)
        {
            return payload.ToArray();
        }

        byte[] dataBody = EncodeSequenceBody(sequenceNumber, payload);
        if (service == RpcSecGssService.Integrity)
        {
            byte[] checksum = context.GetMic(dataBody);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteOpaqueVariable(dataBody);
            writer.WriteOpaqueVariable(checksum);
            return buffer.WrittenSpan.ToArray();
        }

        if (service == RpcSecGssService.Privacy)
        {
            byte[] wrapped = context.Wrap(dataBody);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteOpaqueVariable(wrapped);
            return buffer.WrittenSpan.ToArray();
        }

        throw new RpcException($"Unsupported RPCSEC_GSS service {service}.");
    }

    public static byte[] UnprotectData(
        IGssContext context,
        RpcSecGssService service,
        uint sequenceNumber,
        ReadOnlyMemory<byte> protectedPayload)
    {
        if (service == RpcSecGssService.None)
        {
            return protectedPayload.ToArray();
        }

        byte[] dataBody;
        if (service == RpcSecGssService.Integrity)
        {
            var reader = new XdrReader(protectedPayload.Span);
            dataBody = reader.ReadOpaqueVariable(MaxProtectedBodyLength).ToArray();
            ReadOnlySpan<byte> checksum = reader.ReadOpaqueVariable(MaxTokenLength);
            if (!context.VerifyMic(dataBody, checksum))
            {
                throw new RpcException("RPCSEC_GSS integrity checksum verification failed.");
            }
        }
        else if (service == RpcSecGssService.Privacy)
        {
            var reader = new XdrReader(protectedPayload.Span);
            ReadOnlySpan<byte> wrapped = reader.ReadOpaqueVariable(MaxProtectedBodyLength);
            dataBody = context.Unwrap(wrapped);
        }
        else
        {
            throw new RpcException($"Unsupported RPCSEC_GSS service {service}.");
        }

        return DecodeSequenceBody(sequenceNumber, dataBody);
    }

    public static byte[] EncodeSequenceNumber(uint sequenceNumber)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(sequenceNumber);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeSequenceBody(uint sequenceNumber, ReadOnlySpan<byte> payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(sequenceNumber);
        writer.WriteRaw(payload);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] DecodeSequenceBody(uint expectedSequenceNumber, ReadOnlySpan<byte> dataBody)
    {
        var reader = new XdrReader(dataBody);
        uint sequenceNumber = reader.ReadUInt32();
        if (sequenceNumber != expectedSequenceNumber)
        {
            throw new RpcException(
                $"RPCSEC_GSS sequence {sequenceNumber} does not match expected {expectedSequenceNumber}.");
        }

        return dataBody[reader.Position..].ToArray();
    }
}

internal readonly record struct RpcSecGssCredential(
    RpcSecGssProcedure Procedure,
    uint SequenceNumber,
    RpcSecGssService Service,
    ReadOnlyMemory<byte> Handle);

internal readonly record struct RpcSecGssInitResult(
    ReadOnlyMemory<byte> Handle,
    GssMajorStatus MajorStatus,
    uint MinorStatus,
    uint SequenceWindow,
    ReadOnlyMemory<byte> OutputToken);

internal readonly record struct RpcSecGssRawBody(ReadOnlyMemory<byte> Body) : IXdrSerializable<RpcSecGssRawBody>
{
    public void WriteTo(ref XdrWriter writer) => writer.WriteRaw(Body.Span);

    public static RpcSecGssRawBody ReadFrom(ref XdrReader reader) =>
        new(reader.ReadOpaqueFixed(reader.Remaining).ToArray());
}

internal readonly record struct RpcSecGssInitArgument(ReadOnlyMemory<byte> Token) : IXdrSerializable<RpcSecGssInitArgument>
{
    public void WriteTo(ref XdrWriter writer) => writer.WriteOpaqueVariable(Token.Span);

    public static RpcSecGssInitArgument ReadFrom(ref XdrReader reader) =>
        new(reader.ReadOpaqueVariable(RpcSecGssWire.MaxTokenLength).ToArray());
}
