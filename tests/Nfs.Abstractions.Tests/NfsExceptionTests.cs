using Xunit;

namespace Nfs.Abstractions.Tests;

public sealed class NfsExceptionTests
{
    [Fact]
    public void Default_ReportsServerFault()
    {
        var exception = new NfsException();

        Assert.Equal(NfsStatus.ServerFault, exception.Status);
        Assert.Contains("ServerFault", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageOnly_DefaultsToServerFault()
    {
        var exception = new NfsException("boom");

        Assert.Equal(NfsStatus.ServerFault, exception.Status);
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public void StatusOnly_SetsStatusAndDescribesIt()
    {
        var exception = new NfsException(NfsStatus.StaleHandle);

        Assert.Equal(NfsStatus.StaleHandle, exception.Status);
        Assert.Contains("StaleHandle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusAndMessage_AreBothPreserved()
    {
        var exception = new NfsException(NfsStatus.NoEntry, "missing");

        Assert.Equal(NfsStatus.NoEntry, exception.Status);
        Assert.Equal("missing", exception.Message);
    }

    [Fact]
    public void StatusMessageAndInner_AreAllPreserved()
    {
        var inner = new InvalidOperationException("cause");
        var exception = new NfsException(NfsStatus.AccessDenied, "denied", inner);

        Assert.Equal(NfsStatus.AccessDenied, exception.Status);
        Assert.Equal("denied", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
