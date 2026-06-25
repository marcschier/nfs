namespace Nfs.Abstractions;

/// <summary>
/// A set of attribute changes to apply to an object (modeled on <c>sattr3</c>, RFC 1813). Each
/// property is optional; a <see langword="null"/> value leaves the corresponding attribute
/// unchanged. Timestamps are already resolved: the protocol layer turns "set to server time"
/// requests into concrete values before they reach the backend.
/// </summary>
public readonly record struct NfsSetAttributes
{
    /// <summary>Gets the new protection mode bits, or <see langword="null"/> to leave them unchanged.</summary>
    public uint? Mode { get; init; }

    /// <summary>Gets the new owner user id, or <see langword="null"/> to leave it unchanged.</summary>
    public uint? Uid { get; init; }

    /// <summary>Gets the new owner group id, or <see langword="null"/> to leave it unchanged.</summary>
    public uint? Gid { get; init; }

    /// <summary>Gets the new size, or <see langword="null"/> to leave it unchanged (truncates or extends).</summary>
    public ulong? Size { get; init; }

    /// <summary>Gets the new last-access time, or <see langword="null"/> to leave it unchanged.</summary>
    public NfsTimestamp? AccessTime { get; init; }

    /// <summary>Gets the new last-modification time, or <see langword="null"/> to leave it unchanged.</summary>
    public NfsTimestamp? ModifyTime { get; init; }

    /// <summary>Gets the new NFSv4 ACL, or <see langword="null"/> to leave it unchanged.</summary>
    public IReadOnlyList<NfsAccessControlEntry>? AccessControlList { get; init; }
}
