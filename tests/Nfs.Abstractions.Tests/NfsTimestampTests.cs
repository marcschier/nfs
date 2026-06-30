using Xunit;

namespace Nfs.Abstractions.Tests;

public sealed class NfsTimestampTests
{
    [Fact]
    public void Epoch_IsZero()
    {
        Assert.Equal(default, NfsTimestamp.Epoch);
        Assert.Equal(0u, NfsTimestamp.Epoch.Seconds);
        Assert.Equal(0u, NfsTimestamp.Epoch.Nanoseconds);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(0), NfsTimestamp.Epoch.ToDateTimeOffset());
    }

    [Fact]
    public void FromDateTimeOffset_RoundTrips_At100NanosecondResolution()
    {
        // 1_234_567 ticks of sub-second precision (ticks are 100 ns, the finest DateTimeOffset grain).
        DateTimeOffset original = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(1_234_567);

        NfsTimestamp timestamp = NfsTimestamp.FromDateTimeOffset(original);

        Assert.Equal(1_700_000_000u, timestamp.Seconds);
        Assert.Equal(123_456_700u, timestamp.Nanoseconds); // 1_234_567 ticks * 100 ns/tick.
        Assert.Equal(original, timestamp.ToDateTimeOffset());
    }

    [Fact]
    public void FromDateTimeOffset_AtEpoch_IsZero()
    {
        NfsTimestamp timestamp = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds(0));

        Assert.Equal(NfsTimestamp.Epoch, timestamp);
    }

    [Fact]
    public void FromDateTimeOffset_BeforeEpoch_ClampsToEpoch()
    {
        Assert.Equal(NfsTimestamp.Epoch, NfsTimestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds(-1)));
        Assert.Equal(
            NfsTimestamp.Epoch,
            NfsTimestamp.FromDateTimeOffset(new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void FromDateTimeOffset_IgnoresOffset_UsesInstant()
    {
        var utc = new DateTimeOffset(2021, 11, 14, 22, 13, 20, TimeSpan.Zero);
        var shifted = utc.ToOffset(TimeSpan.FromHours(5)); // Same instant, different offset.

        Assert.Equal(NfsTimestamp.FromDateTimeOffset(utc), NfsTimestamp.FromDateTimeOffset(shifted));
    }

    [Fact]
    public void ToDateTimeOffset_AddsNanosecondsAt100NanosecondResolution()
    {
        var timestamp = new NfsTimestamp(100, 500_000_000); // 0.5 s.

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(100).AddTicks(5_000_000), timestamp.ToDateTimeOffset());
    }
}
