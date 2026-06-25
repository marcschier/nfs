using System.Buffers;

using Xunit;

namespace Nfs.Xdr.Tests;

public sealed class XdrSerializableTests
{
    [Fact]
    public void Codec_RoundTrips_ThroughInstanceAndStaticMembers()
    {
        var value = new Point(7, -3);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Point decoded = Point.ReadFrom(ref reader);

        Assert.Equal(value, decoded);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Codec_RoundTrips_ThroughGenericConstraint()
    {
        // Exercises the static-abstract pattern the source generator relies on: fully generic,
        // monomorphized, and reflection-free.
        var value = new Point(1, 2);
        Point decoded = RoundTrip(value);
        Assert.Equal(value, decoded);
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

    private readonly record struct Point(int X, int Y) : IXdrSerializable<Point>
    {
        public void WriteTo(ref XdrWriter writer)
        {
            writer.WriteInt32(X);
            writer.WriteInt32(Y);
        }

        public static Point ReadFrom(ref XdrReader reader)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            return new Point(x, y);
        }
    }
}
