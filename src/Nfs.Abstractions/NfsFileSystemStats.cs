namespace Nfs.Abstractions;

/// <summary>
/// Dynamic information about the storage backing a file system (modeled on <c>FSSTAT3resok</c>,
/// RFC 1813). Sizes are in bytes; file counts are in objects.
/// </summary>
public readonly record struct NfsFileSystemStats
{
    /// <summary>Gets the total size of the file system.</summary>
    public ulong TotalBytes { get; init; }

    /// <summary>Gets the amount of free space.</summary>
    public ulong FreeBytes { get; init; }

    /// <summary>Gets the amount of free space available to an unprivileged user.</summary>
    public ulong AvailableBytes { get; init; }

    /// <summary>Gets the total number of objects the file system can hold.</summary>
    public ulong TotalFiles { get; init; }

    /// <summary>Gets the number of free object slots.</summary>
    public ulong FreeFiles { get; init; }

    /// <summary>Gets the number of free object slots available to an unprivileged user.</summary>
    public ulong AvailableFiles { get; init; }

    /// <summary>Gets a hint, in seconds, for how long the volatile values above remain accurate.</summary>
    public uint InvariantSeconds { get; init; }
}
