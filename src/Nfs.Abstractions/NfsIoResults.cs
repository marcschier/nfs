namespace Nfs.Abstractions;

/// <summary>The outcome of a read from an <see cref="INfsFileSystem"/>.</summary>
/// <param name="Data">The bytes read (may be shorter than requested).</param>
/// <param name="EndOfFile">Whether the read reached the end of the file.</param>
public readonly record struct NfsReadResult(ReadOnlyMemory<byte> Data, bool EndOfFile);

/// <summary>The outcome of a read into a caller-provided buffer.</summary>
/// <param name="Count">The number of bytes read.</param>
/// <param name="EndOfFile">Whether the read reached the end of the file.</param>
public readonly record struct NfsBufferedReadResult(uint Count, bool EndOfFile);

/// <summary>The outcome of a write to an <see cref="INfsFileSystem"/>.</summary>
/// <param name="Count">The number of bytes written.</param>
public readonly record struct NfsWriteResult(uint Count);

/// <summary>A single entry returned by a directory read.</summary>
/// <param name="Name">The entry name.</param>
/// <param name="FileId">The object's unique identifier within the file system.</param>
/// <param name="Cookie">An opaque continuation token positioned after this entry.</param>
public readonly record struct NfsDirectoryEntry(string Name, ulong FileId, ulong Cookie);

/// <summary>The outcome of a directory read.</summary>
/// <param name="Entries">The entries read.</param>
/// <param name="EndOfStream">Whether the end of the directory was reached.</param>
public readonly record struct NfsDirectoryListing(IReadOnlyList<NfsDirectoryEntry> Entries, bool EndOfStream);
