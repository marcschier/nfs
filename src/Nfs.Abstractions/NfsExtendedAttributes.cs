namespace Nfs.Abstractions;

/// <summary>How SETXATTR should handle existing or missing keys.</summary>
public enum NfsSetExtendedAttributeMode : uint
{
    /// <summary>Create or replace the extended attribute.</summary>
    Either = 0,

    /// <summary>Create the extended attribute; fail if it already exists.</summary>
    Create = 1,

    /// <summary>Replace the extended attribute; fail if it does not exist.</summary>
    Replace = 2,
}

/// <summary>The result of listing extended attributes.</summary>
/// <param name="Names">The returned names.</param>
/// <param name="Cookie">The cookie to resume listing.</param>
/// <param name="EndOfList">Whether all names have been returned.</param>
public readonly record struct NfsExtendedAttributeListing(
    IReadOnlyList<string> Names,
    ulong Cookie,
    bool EndOfList);
