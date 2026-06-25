using Xunit;

namespace Nfs.Xdr.Tests;

public sealed class XdrConstantsTests
{
    [Fact]
    public void BlockSize_IsFourBytes()
    {
        Assert.Equal(4, XdrConstants.BlockSize);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(5, 3)]
    [InlineData(7, 1)]
    [InlineData(8, 0)]
    public void PaddingFor_RoundsUpToTheNextBlock(int length, int expectedPadding)
    {
        Assert.Equal(expectedPadding, XdrConstants.PaddingFor(length));
    }
}
