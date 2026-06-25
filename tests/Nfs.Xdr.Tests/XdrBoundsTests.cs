using Xunit;

namespace Nfs.Xdr.Tests;

public sealed class XdrBoundsTests
{
    [Fact]
    public void ReadInt32_PastEndOfBuffer_Throws()
    {
        static void Act()
        {
            var reader = new XdrReader(new byte[2]);
            reader.ReadInt32();
        }

        Assert.Throws<XdrException>(Act);
    }

    [Fact]
    public void ReadInt64_PastEndOfBuffer_Throws()
    {
        static void Act()
        {
            var reader = new XdrReader(new byte[4]);
            reader.ReadInt64();
        }

        Assert.Throws<XdrException>(Act);
    }

    [Fact]
    public void ReadBool_WithInvalidValue_Throws()
    {
        static void Act()
        {
            var reader = new XdrReader([0x00, 0x00, 0x00, 0x02]);
            reader.ReadBool();
        }

        Assert.Throws<XdrException>(Act);
    }

    [Fact]
    public void ReadOpaqueVariable_LengthExceedsMaximum_Throws()
    {
        static void Act()
        {
            // Encoded length is 5 but the caller permits at most 3.
            var reader = new XdrReader([0x00, 0x00, 0x00, 0x05, 1, 2, 3, 4, 5, 6, 7, 8]);
            reader.ReadOpaqueVariable(maxLength: 3);
        }

        Assert.Throws<XdrException>(Act);
    }

    [Fact]
    public void ReadOpaqueVariable_LengthExceedsBuffer_Throws()
    {
        static void Act()
        {
            // Encoded length is 16 but no data follows the prefix.
            var reader = new XdrReader([0x00, 0x00, 0x00, 0x10]);
            reader.ReadOpaqueVariable(maxLength: int.MaxValue);
        }

        Assert.Throws<XdrException>(Act);
    }

    [Fact]
    public void ReadOpaqueVariable_MissingPadding_Throws()
    {
        static void Act()
        {
            // length 1, one data byte, but the required 3 padding bytes are absent.
            var reader = new XdrReader([0x00, 0x00, 0x00, 0x01, 0xAB]);
            reader.ReadOpaqueVariable(maxLength: int.MaxValue);
        }

        Assert.Throws<XdrException>(Act);
    }
}
