using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RecordMarkingTests
{
    [Fact]
    public void WriteHeader_LastFragment_SetsHighBit()
    {
        byte[] buffer = new byte[RecordMarking.HeaderSize];
        RecordMarking.WriteHeader(buffer, fragmentLength: 100, isLastFragment: true);

        // 0x80000064 = last-fragment flag | 100
        Assert.Equal(new byte[] { 0x80, 0x00, 0x00, 0x64 }, buffer);
    }

    [Fact]
    public void WriteHeader_NonFinalFragment_ClearsHighBit()
    {
        byte[] buffer = new byte[RecordMarking.HeaderSize];
        RecordMarking.WriteHeader(buffer, fragmentLength: 100, isLastFragment: false);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x64 }, buffer);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(100, true)]
    [InlineData(RecordMarking.MaxFragmentLength, false)]
    [InlineData(RecordMarking.MaxFragmentLength, true)]
    public void WriteHeader_ThenParseHeader_RoundTrips(int length, bool isLast)
    {
        byte[] buffer = new byte[RecordMarking.HeaderSize];
        RecordMarking.WriteHeader(buffer, length, isLast);

        (int parsedLength, bool parsedIsLast) = RecordMarking.ParseHeader(buffer);

        Assert.Equal(length, parsedLength);
        Assert.Equal(isLast, parsedIsLast);
    }

    [Fact]
    public void ParseHeader_LastFragmentFlagOnly_IsZeroLengthFinalFragment()
    {
        (int length, bool isLast) = RecordMarking.ParseHeader([0x80, 0x00, 0x00, 0x00]);

        Assert.Equal(0, length);
        Assert.True(isLast);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void WriteHeader_NegativeLength_Throws(int length)
    {
        byte[] buffer = new byte[RecordMarking.HeaderSize];
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordMarking.WriteHeader(buffer, length, true));
    }
}
