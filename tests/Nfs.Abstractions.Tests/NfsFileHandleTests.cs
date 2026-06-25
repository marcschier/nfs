using Xunit;

namespace Nfs.Abstractions.Tests;

public sealed class NfsFileHandleTests
{
    [Fact]
    public void DefaultHandle_IsEmpty()
    {
        NfsFileHandle handle = default;

        Assert.True(handle.IsEmpty);
        Assert.Equal(0, handle.Length);
        Assert.Equal(0, handle.Span.Length);
    }

    [Fact]
    public void Handle_PreservesBytes()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var handle = new NfsFileHandle(bytes);

        Assert.Equal(5, handle.Length);
        Assert.Equal(bytes, handle.ToArray());
    }

    [Fact]
    public void Handle_CopiesInput_SoLaterMutationDoesNotLeak()
    {
        byte[] bytes = [1, 2, 3];
        var handle = new NfsFileHandle(bytes);
        bytes[0] = 0xFF;

        Assert.Equal(new byte[] { 1, 2, 3 }, handle.ToArray());
    }

    [Fact]
    public void Handles_WithSameBytes_AreEqual()
    {
        var a = new NfsFileHandle([10, 20, 30]);
        var b = new NfsFileHandle([10, 20, 30]);

        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Handles_WithDifferentBytes_AreNotEqual()
    {
        var a = new NfsFileHandle([1, 2, 3]);
        var b = new NfsFileHandle([1, 2, 4]);

        Assert.True(a != b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Handle_LongerThanMaximum_Throws()
    {
        byte[] tooLong = new byte[NfsFileHandle.MaxLength + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => new NfsFileHandle(tooLong));
    }

    [Fact]
    public void Handle_AtMaximumLength_IsAccepted()
    {
        byte[] max = new byte[NfsFileHandle.MaxLength];
        var handle = new NfsFileHandle(max);

        Assert.Equal(NfsFileHandle.MaxLength, handle.Length);
    }
}
