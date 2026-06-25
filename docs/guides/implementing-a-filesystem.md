# Implementing a file system

A server never touches storage directly. It is given an `INfsFileSystem` (from `Nfs.Abstractions`) and translates protocol requests into calls on that interface. To export your own storage — a database, an object store, a virtual tree — implement `INfsFileSystem`.

## The contract

Objects are identified by opaque `NfsFileHandle` values that your implementation mints and interprets. A handle is an immutable byte sequence of at most 64 bytes; it must be stable for as long as a client might cache it and must not be forgeable into a handle for an object the client should not reach.

Failures are reported by throwing `NfsException` with the appropriate `NfsStatus`. For example, a lookup of a missing name throws `new NfsException(NfsStatus.NoEntry)`; a handle you no longer recognize throws `NfsStatus.StaleHandle`. The protocol layer maps that status onto each NFS version's wire codes, so you never deal with version-specific status numbers.

## Required members

Every backend must implement the core operations:

```csharp
ValueTask<NfsFileHandle> GetRootHandleAsync(CancellationToken ct = default);
ValueTask<NfsFileAttributes> GetAttributesAsync(NfsFileHandle handle, CancellationToken ct = default);
ValueTask<NfsFileHandle> LookupAsync(NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask<NfsReadResult> ReadAsync(NfsFileHandle file, ulong offset, uint count, CancellationToken ct = default);
ValueTask<NfsWriteResult> WriteAsync(NfsFileHandle file, ulong offset, ReadOnlyMemory<byte> data, CancellationToken ct = default);
ValueTask<NfsFileHandle> CreateAsync(NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask<NfsFileHandle> MakeDirectoryAsync(NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask RemoveAsync(NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask RemoveDirectoryAsync(NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask<NfsDirectoryListing> ReadDirectoryAsync(NfsFileHandle directory, ulong cookie, uint count, CancellationToken ct = default);
```

## Optional members

The remaining operations have default implementations that throw `NfsStatus.NotSupported` (or, for file-system statistics, return zeroes). Override them to add capability:

```csharp
ValueTask<NfsFileAttributes> SetAttributesAsync(NfsFileHandle handle, NfsSetAttributes attributes, CancellationToken ct = default);
ValueTask RenameAsync(NfsFileHandle srcDir, string srcName, NfsFileHandle dstDir, string dstName, CancellationToken ct = default);
ValueTask<NfsFileHandle> CreateSymbolicLinkAsync(NfsFileHandle directory, string name, string target, CancellationToken ct = default);
ValueTask<string> ReadSymbolicLinkAsync(NfsFileHandle handle, CancellationToken ct = default);
ValueTask CreateHardLinkAsync(NfsFileHandle target, NfsFileHandle directory, string name, CancellationToken ct = default);
ValueTask<NfsFileSystemStats> GetFileSystemStatsAsync(NfsFileHandle handle, CancellationToken ct = default);
```

A backend that omits, say, symbolic links will correctly answer real clients with `NFS*ERR_NOTSUPP` for `SYMLINK`/`READLINK` while serving everything else. The same pattern covers the rest of the optional surface — parent lookup (`LookupParentAsync`), special-node creation (`MakeSpecialNodeAsync`), NFSv4 ACLs (`GetAccessControlListAsync` / `SetAccessControlListAsync`), and RFC 8276 extended attributes — each of which defaults to `NFS*ERR_NOTSUPP` until you override it.

## Directory cookies

`ReadDirectoryAsync` takes an opaque `cookie` and returns each entry with the cookie that resumes enumeration **after** that entry. A cookie of `0` starts at the beginning. The cookie space is yours to define, but it must be stable enough that a client can resume a partially-read directory; the in-box backends use the entry's ordinal position. NFS v4 reserves cookie values 0, 1, and 2, so a v4-facing backend should avoid issuing them as real cookies.

## Attributes

`NfsFileAttributes` is the version-independent attribute record (modeled on `fattr3`). Fill in the type, mode, link count, owner/group ids, size, used bytes, file id, and the access/modify/change timestamps (`NfsTimestamp`). The protocol layer derives each version's representation — including the NFS v4 `change` attribute and `fattr4` bitmap — from these fields.

## A minimal example

```csharp
using Nfs.Abstractions;

public sealed class SingleFileSystem : INfsFileSystem
{
    private static readonly NfsFileHandle RootHandle = new([0]);
    private static readonly NfsFileHandle FileHandle = new([1]);
    private readonly byte[] _content = "hello, world\n"u8.ToArray();

    public ValueTask<NfsFileHandle> GetRootHandleAsync(CancellationToken ct = default) => new(RootHandle);

    public ValueTask<NfsFileAttributes> GetAttributesAsync(NfsFileHandle handle, CancellationToken ct = default)
    {
        if (handle == RootHandle)
        {
            return new(new NfsFileAttributes { Type = NfsFileType.Directory, Mode = 0x1ED, LinkCount = 2 });
        }

        if (handle == FileHandle)
        {
            return new(new NfsFileAttributes
            {
                Type = NfsFileType.Regular,
                Mode = 0x1A4,
                Size = (ulong)_content.Length,
                FileId = 1,
            });
        }

        throw new NfsException(NfsStatus.StaleHandle);
    }

    public ValueTask<NfsFileHandle> LookupAsync(NfsFileHandle directory, string name, CancellationToken ct = default) =>
        directory == RootHandle && name == "hello.txt"
            ? new(FileHandle)
            : throw new NfsException(NfsStatus.NoEntry);

    public ValueTask<NfsReadResult> ReadAsync(NfsFileHandle file, ulong offset, uint count, CancellationToken ct = default)
    {
        if (file != FileHandle)
        {
            throw new NfsException(NfsStatus.StaleHandle);
        }

        if (offset >= (ulong)_content.Length)
        {
            return new(new NfsReadResult(ReadOnlyMemory<byte>.Empty, EndOfFile: true));
        }

        int start = (int)offset;
        int length = (int)Math.Min(count, (uint)(_content.Length - start));
        return new(new NfsReadResult(_content.AsMemory(start, length), start + length >= _content.Length));
    }

    // Mutating operations throw NfsStatus.NotSupported for a read-only export; the
    // remaining required members would reject everything but the single file above.
    public ValueTask<NfsWriteResult> WriteAsync(NfsFileHandle file, ulong offset, ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        throw new NfsException(NfsStatus.ReadOnlyFileSystem);

    public ValueTask<NfsFileHandle> CreateAsync(NfsFileHandle directory, string name, CancellationToken ct = default) =>
        throw new NfsException(NfsStatus.ReadOnlyFileSystem);

    public ValueTask<NfsFileHandle> MakeDirectoryAsync(NfsFileHandle directory, string name, CancellationToken ct = default) =>
        throw new NfsException(NfsStatus.ReadOnlyFileSystem);

    public ValueTask RemoveAsync(NfsFileHandle directory, string name, CancellationToken ct = default) =>
        throw new NfsException(NfsStatus.ReadOnlyFileSystem);

    public ValueTask RemoveDirectoryAsync(NfsFileHandle directory, string name, CancellationToken ct = default) =>
        throw new NfsException(NfsStatus.ReadOnlyFileSystem);

    public ValueTask<NfsDirectoryListing> ReadDirectoryAsync(NfsFileHandle directory, ulong cookie, uint count, CancellationToken ct = default)
    {
        if (directory != RootHandle)
        {
            throw new NfsException(NfsStatus.NotDirectory);
        }

        IReadOnlyList<NfsDirectoryEntry> entries = cookie == 0
            ? [new NfsDirectoryEntry("hello.txt", FileId: 1, Cookie: 1)]
            : [];
        return new(new NfsDirectoryListing(entries, EndOfStream: true));
    }
}
```

For a fuller, mutable reference, read `InMemoryFileSystem`; for a disk-backed example with path-traversal guards and real asynchronous I/O, read `LocalDiskFileSystem`. Both live in `Nfs.Server`.
