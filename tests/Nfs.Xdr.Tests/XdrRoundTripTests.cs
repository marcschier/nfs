using System.Buffers;

using Xunit;

namespace Nfs.Xdr.Tests;

public sealed class XdrRoundTripTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Int32_RoundTrips(int value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteInt32(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadInt32());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    [InlineData(0xDEADBEEFu)]
    public void UInt32_RoundTrips(uint value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadUInt32());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Int64_RoundTrips(long value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteInt64(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadInt64());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x1122334455667788ul)]
    public void UInt64_RoundTrips(ulong value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt64(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadUInt64());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bool_RoundTrips(bool value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteBool(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadBool());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1.5f)]
    [InlineData(float.MaxValue)]
    public void Single_RoundTrips(float value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteSingle(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadSingle());
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(3.14159265358979d)]
    [InlineData(double.MinValue)]
    public void Double_RoundTrips(double value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteDouble(value);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadDouble());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(16)]
    public void OpaqueVariable_RoundTrips_AndStaysBlockAligned(int length)
    {
        byte[] data = CreateBytes(length);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueVariable(data);
        Assert.Equal(0, writer.BytesWritten % XdrConstants.BlockSize);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.True(reader.ReadOpaqueVariable(int.MaxValue).SequenceEqual(data));
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void OpaqueFixed_RoundTrips_AndStaysBlockAligned(int length)
    {
        byte[] data = CreateBytes(length);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteOpaqueFixed(data);
        Assert.Equal(0, writer.BytesWritten % XdrConstants.BlockSize);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.True(reader.ReadOpaqueFixed(length).SequenceEqual(data));
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    [InlineData("test")]
    [InlineData("hello")]
    [InlineData("héllo wörld")]
    public void Utf8String_RoundTrips(string value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteString(value);
        Assert.Equal(0, writer.BytesWritten % XdrConstants.BlockSize);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadString(int.MaxValue));
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void MultipleValues_RoundTripInOrder()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteInt32(42);
        writer.WriteString("nfs");
        writer.WriteBool(true);
        writer.WriteInt64(0x1122334455667788);

        var reader = new XdrReader(buffer.WrittenSpan);
        Assert.Equal(42, reader.ReadInt32());
        Assert.Equal("nfs", reader.ReadString(64));
        Assert.True(reader.ReadBool());
        Assert.Equal(0x1122334455667788, reader.ReadInt64());
        Assert.Equal(0, reader.Remaining);
    }

    private static byte[] CreateBytes(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(i + 1);
        }

        return data;
    }
}
