namespace Nfs.Abstractions;

/// <summary>
/// A timestamp with nanosecond resolution, expressed as whole seconds and nanoseconds since the
/// Unix epoch — matching the NFS wire representation (<c>nfstime3</c>).
/// </summary>
/// <param name="Seconds">Whole seconds since the Unix epoch.</param>
/// <param name="Nanoseconds">Nanoseconds within the second.</param>
public readonly record struct NfsTimestamp(uint Seconds, uint Nanoseconds)
{
    /// <summary>Gets the timestamp at the Unix epoch.</summary>
    public static NfsTimestamp Epoch => default;

    /// <summary>Converts a <see cref="DateTimeOffset"/> to a timestamp, truncating to nanoseconds.</summary>
    /// <param name="value">The value to convert (values before the epoch clamp to it).</param>
    /// <returns>The timestamp.</returns>
    public static NfsTimestamp FromDateTimeOffset(DateTimeOffset value)
    {
        long seconds = value.ToUnixTimeSeconds();
        if (seconds < 0)
        {
            return Epoch;
        }

        uint nanoseconds = (uint)((value.Ticks % TimeSpan.TicksPerSecond) * 100);
        return new NfsTimestamp((uint)seconds, nanoseconds);
    }

    /// <summary>Converts this timestamp to a <see cref="DateTimeOffset"/> (at 100-nanosecond resolution).</summary>
    /// <returns>The equivalent <see cref="DateTimeOffset"/>.</returns>
    public DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanoseconds / 100);
}
