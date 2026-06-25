using System.Buffers;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Xdr.Generator.Tests;

public sealed class GeneratedCodecTests
{
    [Fact]
    public void Primitives_RoundTrips()
    {
        var value = new Primitives { A = -5, B = 0xDEADBEEF, C = long.MinValue, D = ulong.MaxValue, E = true };
        Primitives decoded = RoundTrip(value);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Primitives_ProduceExpectedWireBytes()
    {
        var value = new Primitives { A = 1, B = 2, C = 3, D = 4, E = true };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        byte[] expected = Convert.FromHexString(
            "00000001" +          // A = 1
            "00000002" +          // B = 2
            "0000000000000003" +  // C = 3 (hyper)
            "0000000000000004" +  // D = 4 (unsigned hyper)
            "00000001");          // E = true
        Assert.Equal(expected, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void EnumStringOpaque_RoundTrips()
    {
        var value = new EnumStringOpaque
        {
            Color = Color.Blue,
            Name = "nfs",
            Data = [1, 2, 3, 4, 5],
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        EnumStringOpaque decoded = EnumStringOpaque.ReadFrom(ref reader);

        Assert.Equal(Color.Blue, decoded.Color);
        Assert.Equal("nfs", decoded.Name);
        Assert.Equal(value.Data, decoded.Data);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void FileHandle_FixedOpaque_RoundTrips()
    {
        var value = new FileHandle { Bytes = [1, 2, 3, 4, 5, 6, 7, 8] };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        // Fixed opaque has no length prefix; eight bytes are already block-aligned.
        Assert.Equal(8, writer.BytesWritten);

        var reader = new XdrReader(buffer.WrittenSpan);
        FileHandle decoded = FileHandle.ReadFrom(ref reader);
        Assert.Equal(value.Bytes, decoded.Bytes);
    }

    [Fact]
    public void Nested_SerializableField_RoundTrips()
    {
        var value = new Nested
        {
            Tag = 7,
            Inner = new Primitives { A = 1, B = 2, C = 3, D = 4, E = false },
        };

        Nested decoded = RoundTrip(value);

        Assert.Equal(7, decoded.Tag);
        Assert.Equal(value.Inner, decoded.Inner);
    }

    [Fact]
    public void Optionals_WhenPresent_RoundTrip()
    {
        var value = new WithOptionals
        {
            MaybeInt = 42,
            MaybeStruct = new Primitives { A = 1, B = 2, C = 3, D = 4, E = true },
            MaybeColor = Color.Green,
        };

        WithOptionals decoded = RoundTrip(value);

        Assert.Equal(42, decoded.MaybeInt);
        Assert.Equal(value.MaybeStruct, decoded.MaybeStruct);
        Assert.Equal(Color.Green, decoded.MaybeColor);
    }

    [Fact]
    public void Optionals_WhenAbsent_RoundTrip()
    {
        WithOptionals decoded = RoundTrip(default(WithOptionals));

        Assert.Null(decoded.MaybeInt);
        Assert.Null(decoded.MaybeStruct);
        Assert.Null(decoded.MaybeColor);
    }

    [Fact]
    public void Optional_ProducesBoolDiscriminatedWireBytes()
    {
        var value = new WithOptionals { MaybeInt = 7 };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        byte[] expected = Convert.FromHexString(
            "00000001" + "00000007" +  // MaybeInt present, value 7
            "00000000" +               // MaybeStruct absent
            "00000000");               // MaybeColor absent
        Assert.Equal(expected, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Arrays_RoundTrip()
    {
        var value = new WithArrays
        {
            Numbers = [1, 2, 3],
            Items =
            [
                new Primitives { A = 1, B = 2, C = 3, D = 4, E = true },
                new Primitives { A = 5, B = 6, C = 7, D = 8, E = false },
            ],
        };

        WithArrays decoded = RoundTrip(value);

        Assert.Equal(value.Numbers, decoded.Numbers);
        Assert.Equal(2, decoded.Items.Length);
        Assert.Equal(value.Items[0], decoded.Items[0]);
        Assert.Equal(value.Items[1], decoded.Items[1]);
    }

    [Fact]
    public void Array_ProducesCountPrefixedWireBytes()
    {
        var value = new WithArrays { Numbers = [10, 20], Items = [] };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        byte[] expected = Convert.FromHexString(
            "00000002" + "0000000A" + "00000014" +  // Numbers: count 2, [10, 20]
            "00000000");                            // Items: count 0
        Assert.Equal(expected, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Array_NullField_EncodesAsEmpty()
    {
        var value = new WithArrays { Numbers = [], Items = null! };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        Assert.Equal(Convert.FromHexString("00000000" + "00000000"), buffer.WrittenSpan.ToArray());
    }

    private static T RoundTrip<T>(T value)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        return T.ReadFrom(ref reader);
    }
}
