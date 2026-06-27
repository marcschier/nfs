using System.Buffers;

namespace Nfs.Abstractions;

/// <summary>
/// A pluggable storage backend that an NFS server exposes. Implementations identify objects by
/// opaque <see cref="NfsFileHandle"/> values and signal failures by throwing
/// <see cref="NfsException"/> with the appropriate <see cref="NfsStatus"/>.
/// </summary>
public interface INfsFileSystem
{
    /// <summary>Gets the handle of the file system's root directory.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The root handle.</returns>
    ValueTask<NfsFileHandle> GetRootHandleAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the attributes of the object identified by <paramref name="handle"/>.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The object's attributes.</returns>
    /// <exception cref="NfsException">The handle is stale or the object is inaccessible.</exception>
    ValueTask<NfsFileAttributes> GetAttributesAsync(NfsFileHandle handle, CancellationToken cancellationToken = default);

    /// <summary>Resolves <paramref name="name"/> within the directory <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="name">The name to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handle of the named object.</returns>
    /// <exception cref="NfsException">
    /// The directory is stale or not a directory, or the name does not exist.
    /// </exception>
    ValueTask<NfsFileHandle> LookupAsync(NfsFileHandle directory, string name, CancellationToken cancellationToken = default);

    /// <summary>Resolves the parent directory of <paramref name="handle"/>.</summary>
    /// <param name="handle">The handle whose parent directory should be returned.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The parent directory handle.</returns>
    /// <exception cref="NfsException">The handle is stale or already names the export root.</exception>
    ValueTask<NfsFileHandle> LookupParentAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Reads up to <paramref name="count"/> bytes from a file starting at an offset.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bytes read and whether the end of the file was reached.</returns>
    /// <exception cref="NfsException">The handle is stale or does not name a regular file.</exception>
    ValueTask<NfsReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default);

    /// <summary>Reads into <paramref name="destination"/> without requiring the backend to allocate the data buffer.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="destination">The destination for bytes read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The byte count read and whether the end of the file was reached.</returns>
    /// <exception cref="NfsException">The handle is stale or does not name a regular file.</exception>
#if NETSTANDARD2_0
    ValueTask<NfsBufferedReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default);
#else
    async ValueTask<NfsBufferedReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        NfsReadResult read = await ReadAsync(file, offset, count, cancellationToken).ConfigureAwait(false);
        destination.Write(read.Data.Span);
        return new NfsBufferedReadResult((uint)read.Data.Length, read.EndOfFile);
    }
#endif

    /// <summary>Writes <paramref name="data"/> to a file starting at an offset.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to write at.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="NfsException">The handle is stale or does not name a regular file.</exception>
    ValueTask<NfsWriteResult> WriteAsync(
        NfsFileHandle file,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an empty regular file in a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new file's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handle of the new file.</returns>
    /// <exception cref="NfsException">The parent is invalid or the name already exists.</exception>
    ValueTask<NfsFileHandle> CreateAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new directory's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handle of the new directory.</returns>
    /// <exception cref="NfsException">The parent is invalid or the name already exists.</exception>
    ValueTask<NfsFileHandle> MakeDirectoryAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a special node in a directory, when the backend supports that object type.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new node's name.</param>
    /// <param name="type">The special node type to create.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handle of the new node.</returns>
    /// <exception cref="NfsException">The parent is invalid, the name exists, or the type is unsupported.</exception>
    ValueTask<NfsFileHandle> MakeSpecialNodeAsync(
        NfsFileHandle directory,
        string name,
        NfsFileType type,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Removes a regular file from a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is removed.</returns>
    /// <exception cref="NfsException">The name does not exist or is a directory.</exception>
    ValueTask RemoveAsync(NfsFileHandle directory, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes an empty sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The sub-directory's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory is removed.</returns>
    /// <exception cref="NfsException">The name does not exist, is not a directory, or is not empty.</exception>
    ValueTask RemoveDirectoryAsync(NfsFileHandle directory, string name, CancellationToken cancellationToken = default);

    /// <summary>Reads the entries of a directory, continuing from an opaque cookie.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="cookie">A continuation token from a previous read, or 0 to start at the beginning.</param>
    /// <param name="count">A hint for the maximum number of bytes of entries to return.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The entries and whether the end of the directory was reached.</returns>
    /// <exception cref="NfsException">The handle is stale or does not name a directory.</exception>
    ValueTask<NfsDirectoryListing> ReadDirectoryAsync(
        NfsFileHandle directory,
        ulong cookie,
        uint count,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a set of attribute changes to an object.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="attributes">The changes to apply.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The object's attributes after the change.</returns>
    /// <exception cref="NfsException">The handle is stale or the change is not permitted.</exception>
    ValueTask<NfsFileAttributes> SetAttributesAsync(
        NfsFileHandle handle,
        NfsSetAttributes attributes,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Moves or renames an object between directories.</summary>
    /// <param name="sourceDirectory">The handle of the directory currently holding the object.</param>
    /// <param name="sourceName">The object's current name.</param>
    /// <param name="targetDirectory">The handle of the destination directory.</param>
    /// <param name="targetName">The object's new name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the object is renamed.</returns>
    /// <exception cref="NfsException">A handle is invalid or the source name does not exist.</exception>
    ValueTask RenameAsync(
        NfsFileHandle sourceDirectory,
        string sourceName,
        NfsFileHandle targetDirectory,
        string targetName,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Creates a symbolic link.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="target">The path the link points to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The handle of the new link.</returns>
    /// <exception cref="NfsException">The parent is invalid or the name already exists.</exception>
    ValueTask<NfsFileHandle> CreateSymbolicLinkAsync(
        NfsFileHandle directory,
        string name,
        string target,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Reads the target path of a symbolic link.</summary>
    /// <param name="handle">The link's handle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The path the link points to.</returns>
    /// <exception cref="NfsException">The handle is stale or does not name a symbolic link.</exception>
    ValueTask<string> ReadSymbolicLinkAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Creates a hard link to an existing object.</summary>
    /// <param name="target">The handle of the existing object to link to.</param>
    /// <param name="directory">The directory that will hold the new link.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the link is created.</returns>
    /// <exception cref="NfsException">A handle is invalid or the name already exists.</exception>
    ValueTask CreateHardLinkAsync(
        NfsFileHandle target,
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif


    /// <summary>Gets the NFSv4 ACL associated with an object.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The object's ACL entries in evaluation order.</returns>
    ValueTask<IReadOnlyList<NfsAccessControlEntry>> GetAccessControlListAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        new(Array.Empty<NfsAccessControlEntry>());
#endif

    /// <summary>Sets the NFSv4 ACL associated with an object.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="entries">The ACL entries in evaluation order.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the ACL is stored.</returns>
    ValueTask SetAccessControlListAsync(
        NfsFileHandle handle,
        IReadOnlyList<NfsAccessControlEntry> entries,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Gets an extended attribute value.</summary>
    ValueTask<byte[]> GetExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Sets an extended attribute value.</summary>
    ValueTask SetExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        ReadOnlyMemory<byte> value,
        NfsSetExtendedAttributeMode mode,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Lists extended attribute names.</summary>
    ValueTask<NfsExtendedAttributeListing> ListExtendedAttributesAsync(
        NfsFileHandle handle,
        ulong cookie,
        uint maxCount,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Removes an extended attribute value.</summary>
    ValueTask RemoveExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        throw new NfsException(NfsStatus.NotSupported);
#endif

    /// <summary>Gets dynamic information about the file system containing a handle.</summary>
    /// <param name="handle">A handle within the file system (typically the export root).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The file-system statistics.</returns>
    /// <exception cref="NfsException">The handle is stale.</exception>
    ValueTask<NfsFileSystemStats> GetFileSystemStatsAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        =>
        new(default(NfsFileSystemStats));
#endif
}
