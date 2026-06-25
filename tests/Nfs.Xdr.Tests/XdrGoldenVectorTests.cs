using System.Buffers;

using Xunit;

namespace Nfs.Xdr.Tests;

/// <summary>
/// Pins the exact on-the-wire byte encoding of each primitive against hand-verified vectors
/// (RFC 4506). These guard against accidental endianness, padding, or length-prefix regressions.
/// </summary>
public sealed class XdrGoldenVectorTests
{
    [Fact]
    public void Int32_Vectors()
    {
        Assert.Equal(Hex("00000000"), WriteInt32(0));
        Assert.Equal(Hex("00000001"), WriteInt32(1));
        Assert.Equal(Hex("FFFFFFFF"), WriteInt32(-1));
        Assert.Equal(Hex("7FFFFFFF"), WriteInt32(int.MaxValue));
        Assert.Equal(Hex("80000000"), WriteInt32(int.MinValue));
    }

    [Fact]
    public void UInt32_Vectors()
    {
        Assert.Equal(Hex("DEADBEEF"), WriteUInt32(0xDEADBEEF));
        Assert.Equal(Hex("FFFFFFFF"), WriteUInt32(uint.MaxValue));
    }

    [Fact]
    public void Int64_Vectors()
    {
        Assert.Equal(Hex("0000000000000001"), WriteInt64(1));
        Assert.Equal(Hex("FFFFFFFFFFFFFFFF"), WriteInt64(-1));
    }

    [Fact]
    public void UInt64_Vectors()
    {
        Assert.Equal(Hex("1122334455667788"), WriteUInt64(0x1122334455667788));
    }

    [Fact]
    public void Bool_Vectors()
    {
        Assert.Equal(Hex("00000001"), WriteBool(true));
        Assert.Equal(Hex("00000000"), WriteBool(false));
    }

    [Fact]
    public void Single_Vector()
    {
        Assert.Equal(Hex("3F800000"), WriteSingle(1.0f));
    }

    [Fact]
    public void Double_Vector()
    {
        Assert.Equal(Hex("3FF0000000000000"), WriteDouble(1.0d));
    }

    [Fact]
    public void OpaqueVariable_IncludesLengthPrefixAndPadding()
    {
        // length 3 (0x00000003), bytes 'a','b','c', then one pad byte.
        Assert.Equal(Hex("00000003 61626300"), WriteOpaqueVariable([0x61, 0x62, 0x63]));
        Assert.Equal(Hex("00000000"), WriteOpaqueVariable([]));
    }

    [Fact]
    public void OpaqueFixed_HasNoLengthPrefix()
    {
        Assert.Equal(Hex("61626300"), WriteOpaqueFixed([0x61, 0x62, 0x63]));
        Assert.Equal(Hex("61626364"), WriteOpaqueFixed([0x61, 0x62, 0x63, 0x64]));
    }

    [Fact]
    public void String_IncludesLengthPrefixAndPadding()
    {
        // "hello" => length 5, 5 bytes, then 3 pad bytes => 12 bytes total.
        Assert.Equal(Hex("00000005 68656C6C 6F000000"), WriteString("hello"));
        Assert.Equal(Hex("00000004 74657374"), WriteString("test"));
        Assert.Equal(Hex("00000000"), WriteString(string.Empty));
    }

    [Fact]
    public void Decode_FromKnownBytes()
    {
        var reader = new XdrReader(Hex("FFFFFFFF"));
        Assert.Equal(-1, reader.ReadInt32());

        var boolReader = new XdrReader(Hex("00000001"));
        Assert.True(boolReader.ReadBool());

        var strReader = new XdrReader(Hex("00000005 68656C6C 6F000000"));
        Assert.Equal("hello", strReader.ReadString(16));
        Assert.Equal(0, strReader.Remaining);
    }

    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    private static byte[] WriteInt32(int value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteInt32(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteUInt32(uint value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteInt64(long value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteInt64(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteUInt64(ulong value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt64(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteBool(bool value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteBool(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteSingle(float value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteSingle(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteDouble(double value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteDouble(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteOpaqueVariable(byte[] value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueVariable(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteOpaqueFixed(byte[] value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueFixed(value);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteString(string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteString(value);
        return buffer.WrittenSpan.ToArray();
    }
}
