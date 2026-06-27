using System.Buffers;
using System.Buffers.Binary;

using Nfs.Abstractions;

namespace Nfs.Server;

/// <summary>
/// An <see cref="INfsFileSystem"/> backed by a directory on the local disk. Objects are addressed
/// by an opaque handle that maps to a path relative to the export root; handles are assigned lazily
/// and are stable for the lifetime of the instance (but not across restarts).
/// </summary>
public sealed class LocalDiskFileSystem : INfsFileSystem
{
    private const uint DirectoryMode = 0x1ED;   // 0755
    private const uint RegularFileMode = 0x1A4; // 0644

    private readonly string _rootPath;
    private readonly Dictionary<ulong, string> _idToPath = new();
    private readonly Dictionary<string, ulong> _pathToId = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, IReadOnlyList<NfsAccessControlEntry>> _accessControlLists = new();
    private readonly Dictionary<ulong, Dictionary<string, byte[]>> _extendedAttributes = new();
    private readonly object _gate = new();
    private ulong _nextId;

    /// <summary>Creates a file system rooted at <paramref name="rootPath"/>.</summary>
    /// <param name="rootPath">An existing directory to export.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public LocalDiskFileSystem(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException(_rootPath);
        }

        Root = HandleFor(string.Empty);
    }

    /// <summary>Gets the handle of the export root.</summary>
    public NfsFileHandle Root { get; }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> GetRootHandleAsync(CancellationToken cancellationToken = default) => new(Root);

    /// <inheritdoc/>
    public ValueTask<NfsFileAttributes> GetAttributesAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        (ulong id, string relative) = Resolve(handle);
        string absolute = ToAbsolute(relative);

        if (Directory.Exists(absolute))
        {
            return new ValueTask<NfsFileAttributes>(AttributesFor(new DirectoryInfo(absolute), id));
        }

        if (File.Exists(absolute))
        {
            return new ValueTask<NfsFileAttributes>(AttributesFor(new FileInfo(absolute), id));
        }

        throw new NfsException(NfsStatus.StaleHandle);
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> LookupAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        string childRelative = ResolveChild(Resolve(directory).Relative, name);
        string absolute = ToAbsolute(childRelative);
        if (!File.Exists(absolute) && !Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        return new ValueTask<NfsFileHandle>(HandleFor(childRelative));
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> LookupParentAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        string relative = Resolve(handle).Relative;
        string absolute = ToAbsolute(relative);
        if (!File.Exists(absolute) && !Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.StaleHandle);
        }

        if (relative.Length == 0)
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        int slash = relative.LastIndexOf('/');
        string parentRelative = slash < 0 ? string.Empty : relative[..slash];
        return new ValueTask<NfsFileHandle>(HandleFor(parentRelative));
    }

    /// <inheritdoc/>
    public async ValueTask<NfsReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default)
    {
        string absolute = RequireFile(file);

#if NETSTANDARD2_0
        using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#else
        await using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#endif

        if (offset >= (ulong)stream.Length)
        {
            return new NfsReadResult(ReadOnlyMemory<byte>.Empty, EndOfFile: true);
        }

        stream.Seek((long)offset, SeekOrigin.Begin);
        int toRead = (int)Math.Min(count, (ulong)(stream.Length - (long)offset));
        byte[] buffer = new byte[toRead];
        int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        bool eof = (long)offset + read >= stream.Length;
        ReadOnlyMemory<byte> data = read == buffer.Length ? buffer : buffer.AsMemory(0, read);
        return new NfsReadResult(data, eof);
    }

    /// <inheritdoc/>
    public async ValueTask<NfsBufferedReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        string absolute = RequireFile(file);

#if NETSTANDARD2_0
        using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#else
        await using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#endif

        if (offset >= (ulong)stream.Length)
        {
            return new NfsBufferedReadResult(0, EndOfFile: true);
        }

        stream.Seek((long)offset, SeekOrigin.Begin);
        int toRead = (int)Math.Min(count, (ulong)(stream.Length - (long)offset));
        Memory<byte> memory = destination.GetMemory(toRead);
        int read = await stream.ReadAsync(memory[..toRead], cancellationToken).ConfigureAwait(false);
        destination.Advance(read);

        bool eof = (long)offset + read >= stream.Length;
        return new NfsBufferedReadResult((uint)read, eof);
    }

    /// <inheritdoc/>
    public async ValueTask<NfsWriteResult> WriteAsync(
        NfsFileHandle file,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        string absolute = RequireFile(file);

#if NETSTANDARD2_0
        using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#else
        await using var stream = new FileStream(
            absolute, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#endif

        stream.Seek((long)offset, SeekOrigin.Begin);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        return new NfsWriteResult((uint)data.Length);
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> CreateAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        string childRelative = ResolveChild(RequireDirectory(directory), name);
        string absolute = ToAbsolute(childRelative);
        if (File.Exists(absolute) || Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        File.Create(absolute).Dispose();
        return new ValueTask<NfsFileHandle>(HandleFor(childRelative));
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> MakeDirectoryAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        string childRelative = ResolveChild(RequireDirectory(directory), name);
        string absolute = ToAbsolute(childRelative);
        if (File.Exists(absolute) || Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        Directory.CreateDirectory(absolute);
        return new ValueTask<NfsFileHandle>(HandleFor(childRelative));
    }

    /// <inheritdoc/>
    public ValueTask RemoveAsync(NfsFileHandle directory, string name, CancellationToken cancellationToken = default)
    {
        string childRelative = ResolveChild(RequireDirectory(directory), name);
        string absolute = ToAbsolute(childRelative);
        if (Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.IsDirectory);
        }

        if (!File.Exists(absolute))
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        File.Delete(absolute);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveDirectoryAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        string childRelative = ResolveChild(RequireDirectory(directory), name);
        string absolute = ToAbsolute(childRelative);
        if (!Directory.Exists(absolute))
        {
            throw new NfsException(File.Exists(absolute) ? NfsStatus.NotDirectory : NfsStatus.NoEntry);
        }

        if (Directory.EnumerateFileSystemEntries(absolute).Any())
        {
            throw new NfsException(NfsStatus.DirectoryNotEmpty);
        }

        Directory.Delete(absolute);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsDirectoryListing> ReadDirectoryAsync(
        NfsFileHandle directory,
        ulong cookie,
        uint count,
        CancellationToken cancellationToken = default)
    {
        string relative = RequireDirectory(directory);
        string absolute = ToAbsolute(relative);

        var ordered = Directory.EnumerateFileSystemEntries(absolute)
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        var entries = new List<NfsDirectoryEntry>();
        for (int i = (int)cookie; i < ordered.Count; i++)
        {
            string childRelative = relative.Length == 0 ? ordered[i] : relative + "/" + ordered[i];
            entries.Add(new NfsDirectoryEntry(ordered[i], HandleId(childRelative), (ulong)(i + 1)));
        }

        return new ValueTask<NfsDirectoryListing>(new NfsDirectoryListing(entries, EndOfStream: true));
    }

    /// <inheritdoc/>
    public async ValueTask<NfsFileAttributes> SetAttributesAsync(
        NfsFileHandle handle,
        NfsSetAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        (ulong id, string relative) = Resolve(handle);
        string absolute = ToAbsolute(relative);
        bool isDirectory = Directory.Exists(absolute);
        if (!isDirectory && !File.Exists(absolute))
        {
            throw new NfsException(NfsStatus.StaleHandle);
        }

        if (attributes.Size is { } size)
        {
            if (isDirectory)
            {
                throw new NfsException(NfsStatus.InvalidArgument);
            }

#if NETSTANDARD2_0
            using var stream = new FileStream(
                absolute, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#else
            await using var stream = new FileStream(
                absolute, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
#endif
            stream.SetLength((long)size);
        }

        if (attributes.Mode is { } mode && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(absolute, (UnixFileMode)(mode & 0xFFF));
        }

        if (attributes.AccessTime is { } accessTime)
        {
            File.SetLastAccessTimeUtc(absolute, accessTime.ToDateTimeOffset().UtcDateTime);
        }

        if (attributes.ModifyTime is { } modifyTime)
        {
            File.SetLastWriteTimeUtc(absolute, modifyTime.ToDateTimeOffset().UtcDateTime);
        }

        if (attributes.AccessControlList is { } acl)
        {
            lock (_gate)
            {
                _accessControlLists[id] = [.. acl];
            }
        }

        FileSystemInfo info = isDirectory ? new DirectoryInfo(absolute) : new FileInfo(absolute);
        info.Refresh();
        return AttributesFor(info, id);
    }

    /// <inheritdoc/>
    public ValueTask RenameAsync(
        NfsFileHandle sourceDirectory,
        string sourceName,
        NfsFileHandle targetDirectory,
        string targetName,
        CancellationToken cancellationToken = default)
    {
        string fromRelative = ResolveChild(RequireDirectory(sourceDirectory), sourceName);
        string toRelative = ResolveChild(RequireDirectory(targetDirectory), targetName);
        string fromAbsolute = ToAbsolute(fromRelative);
        string toAbsolute = ToAbsolute(toRelative);

        if (Directory.Exists(fromAbsolute))
        {
            if (File.Exists(toAbsolute))
            {
                throw new NfsException(NfsStatus.NotDirectory);
            }

            if (Directory.Exists(toAbsolute))
            {
                if (Directory.EnumerateFileSystemEntries(toAbsolute).Any())
                {
                    throw new NfsException(NfsStatus.DirectoryNotEmpty);
                }

                Directory.Delete(toAbsolute);
            }

            Directory.Move(fromAbsolute, toAbsolute);
        }
        else if (File.Exists(fromAbsolute))
        {
            if (Directory.Exists(toAbsolute))
            {
                throw new NfsException(NfsStatus.IsDirectory);
            }

            File.Move(fromAbsolute, toAbsolute, overwrite: true);
        }
        else
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> CreateSymbolicLinkAsync(
        NfsFileHandle directory,
        string name,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
#if NET6_0_OR_GREATER
        string childRelative = ResolveChild(RequireDirectory(directory), name);
        string absolute = ToAbsolute(childRelative);
        if (File.Exists(absolute) || Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        try
        {
            File.CreateSymbolicLink(absolute, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new NfsException(NfsStatus.NotSupported);
        }

        return new ValueTask<NfsFileHandle>(HandleFor(childRelative));
#else
        // File.CreateSymbolicLink is net6.0+; symbolic links are unsupported on netstandard.
        throw new NfsException(NfsStatus.NotSupported);
#endif
    }

    /// <inheritdoc/>
    public ValueTask<string> ReadSymbolicLinkAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        string absolute = ToAbsolute(Resolve(handle).Relative);
#if NET6_0_OR_GREATER
        var info = new FileInfo(absolute);
        if (info.LinkTarget is { } target)
        {
            return new ValueTask<string>(target);
        }

        var directoryInfo = new DirectoryInfo(absolute);
        return directoryInfo.LinkTarget is { } directoryTarget
            ? new ValueTask<string>(directoryTarget)
            : throw new NfsException(NfsStatus.InvalidArgument);
#else
        // FileSystemInfo.LinkTarget is net6.0+; symbolic links are unsupported on netstandard.
        _ = absolute;
        throw new NfsException(NfsStatus.NotSupported);
#endif
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<NfsAccessControlEntry>> GetAccessControlListAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        (ulong id, string relative) = Resolve(handle);
        NfsFileAttributes attributes = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            if (_accessControlLists.TryGetValue(id, out IReadOnlyList<NfsAccessControlEntry>? acl))
            {
                return new ValueTask<IReadOnlyList<NfsAccessControlEntry>>(acl);
            }
        }

        return new ValueTask<IReadOnlyList<NfsAccessControlEntry>>(NfsMetadataHelpers.AclFromMode(attributes.Mode));
    }

    /// <inheritdoc/>
    public ValueTask SetAccessControlListAsync(
        NfsFileHandle handle,
        IReadOnlyList<NfsAccessControlEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        (ulong id, string relative) = Resolve(handle);
        _ = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            // LocalDiskFileSystem intentionally keeps NFSv4 ACLs in memory. It does not claim or attempt
            // host OS ACL enforcement because platform ACL models differ from RFC 7530 semantics.
            _accessControlLists[id] = [.. entries];
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<byte[]> GetExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
    {
        NfsMetadataHelpers.ValidateExtendedAttributeName(name);
        (ulong id, string relative) = Resolve(handle);
        _ = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            return _extendedAttributes.TryGetValue(id, out Dictionary<string, byte[]>? values) &&
                values.TryGetValue(name, out byte[]? value)
                    ? new ValueTask<byte[]>([.. value])
                    : throw new NfsException(NfsStatus.NoExtendedAttribute);
        }
    }

    /// <inheritdoc/>
    public ValueTask SetExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        ReadOnlyMemory<byte> value,
        NfsSetExtendedAttributeMode mode,
        CancellationToken cancellationToken = default)
    {
        NfsMetadataHelpers.ValidateExtendedAttributeName(name);
        NfsMetadataHelpers.ValidateExtendedAttributeValue(value);
        (ulong id, string relative) = Resolve(handle);
        _ = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            // Extended attributes are kept in memory for portability; host xattr persistence is not implied.
            if (!_extendedAttributes.TryGetValue(id, out Dictionary<string, byte[]>? values))
            {
                values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                _extendedAttributes[id] = values;
            }

            bool exists = values.ContainsKey(name);
            if (mode == NfsSetExtendedAttributeMode.Create && exists)
            {
                throw new NfsException(NfsStatus.AlreadyExists);
            }

            if (mode == NfsSetExtendedAttributeMode.Replace && !exists)
            {
                throw new NfsException(NfsStatus.NoExtendedAttribute);
            }

            if (!exists && values.Count >= NfsMetadataHelpers.MaxExtendedAttributesPerObject)
            {
                throw new NfsException(NfsStatus.ExtendedAttributeTooBig);
            }

            values[name] = value.ToArray();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsExtendedAttributeListing> ListExtendedAttributesAsync(
        NfsFileHandle handle,
        ulong cookie,
        uint maxCount,
        CancellationToken cancellationToken = default)
    {
        (ulong id, string relative) = Resolve(handle);
        _ = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            IReadOnlyCollection<string> names = _extendedAttributes.TryGetValue(id, out Dictionary<string, byte[]>? values)
                ? values.Keys
                : Array.Empty<string>();
            return new ValueTask<NfsExtendedAttributeListing>(
                NfsMetadataHelpers.ListExtendedAttributes(names, cookie, maxCount));
        }
    }

    /// <inheritdoc/>
    public ValueTask RemoveExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
    {
        NfsMetadataHelpers.ValidateExtendedAttributeName(name);
        (ulong id, string relative) = Resolve(handle);
        _ = GetExistingAttributes(id, relative);
        lock (_gate)
        {
            if (!_extendedAttributes.TryGetValue(id, out Dictionary<string, byte[]>? values) || !values.Remove(name))
            {
                throw new NfsException(NfsStatus.NoExtendedAttribute);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileSystemStats> GetFileSystemStatsAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        _ = Resolve(handle);
        var drive = new DriveInfo(Path.GetPathRoot(_rootPath) ?? _rootPath);
        ulong total = drive.TotalSize > 0 ? (ulong)drive.TotalSize : 0;
        ulong free = drive.AvailableFreeSpace > 0 ? (ulong)drive.AvailableFreeSpace : 0;
        return new ValueTask<NfsFileSystemStats>(new NfsFileSystemStats
        {
            TotalBytes = total,
            FreeBytes = free,
            AvailableBytes = free,
            TotalFiles = uint.MaxValue,
            FreeFiles = uint.MaxValue,
            AvailableFiles = uint.MaxValue,
            InvariantSeconds = 0,
        });
    }

    private NfsFileAttributes GetExistingAttributes(ulong id, string relative)
    {
        string absolute = ToAbsolute(relative);
        if (Directory.Exists(absolute))
        {
            return AttributesFor(new DirectoryInfo(absolute), id);
        }

        if (File.Exists(absolute))
        {
            return AttributesFor(new FileInfo(absolute), id);
        }

        throw new NfsException(NfsStatus.StaleHandle);
    }

    private string RequireFile(NfsFileHandle handle)
    {
        string absolute = ToAbsolute(Resolve(handle).Relative);
        if (Directory.Exists(absolute))
        {
            throw new NfsException(NfsStatus.IsDirectory);
        }

        if (!File.Exists(absolute))
        {
            throw new NfsException(NfsStatus.StaleHandle);
        }

        return absolute;
    }

    private string RequireDirectory(NfsFileHandle handle)
    {
        string relative = Resolve(handle).Relative;
        if (!Directory.Exists(ToAbsolute(relative)))
        {
            throw new NfsException(NfsStatus.NotDirectory);
        }

        return relative;
    }

    private NfsFileHandle HandleFor(string relative)
    {
        ulong id = HandleId(relative);
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, id);
        return new NfsFileHandle(bytes);
    }

    private ulong HandleId(string relative)
    {
        lock (_gate)
        {
            if (!_pathToId.TryGetValue(relative, out ulong id))
            {
                id = ++_nextId;
                _pathToId[relative] = id;
                _idToPath[id] = relative;
            }

            return id;
        }
    }

    private (ulong Id, string Relative) Resolve(NfsFileHandle handle)
    {
        if (handle.Length != 8)
        {
            throw new NfsException(NfsStatus.BadHandle);
        }

        ulong id = BinaryPrimitives.ReadUInt64BigEndian(handle.Span);
        lock (_gate)
        {
            if (_idToPath.TryGetValue(id, out string? relative))
            {
                return (id, relative);
            }
        }

        throw new NfsException(NfsStatus.StaleHandle);
    }

    private string ToAbsolute(string relative)
    {
        if (relative.Length == 0)
        {
            return _rootPath;
        }

        string combined = Path.GetFullPath(Path.Combine(_rootPath, relative));
        if (combined.Length < _rootPath.Length ||
            !combined.AsSpan(0, _rootPath.Length).SequenceEqual(_rootPath) ||
            (combined.Length > _rootPath.Length && combined[_rootPath.Length] != Path.DirectorySeparatorChar))
        {
            throw new NfsException(NfsStatus.AccessDenied);
        }

        return combined;
    }

    private static string ResolveChild(string parentRelative, string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
        {
            throw new NfsException(NfsStatus.InvalidArgument);
        }

        return parentRelative.Length == 0 ? name : parentRelative + "/" + name;
    }

    private static NfsFileAttributes AttributesFor(FileSystemInfo info, ulong id)
    {
        bool isDirectory = info is DirectoryInfo;
#if NET6_0_OR_GREATER
        bool isSymlink = info.LinkTarget is not null;
        long size = isSymlink
            ? System.Text.Encoding.UTF8.GetByteCount(info.LinkTarget!)
            : isDirectory ? 4096 : ((FileInfo)info).Length;
#else
        // FileSystemInfo.LinkTarget is net6.0+; symbolic links are not detected on netstandard.
        bool isSymlink = false;
        long size = isDirectory ? 4096 : ((FileInfo)info).Length;
#endif
        NfsFileType type = isSymlink
            ? NfsFileType.SymbolicLink
            : isDirectory ? NfsFileType.Directory : NfsFileType.Regular;
        return new NfsFileAttributes
        {
            Type = type,
            Mode = isDirectory ? DirectoryMode : RegularFileMode,
            LinkCount = isDirectory ? 2u : 1u,
            Size = (ulong)size,
            Used = (ulong)size,
            FileId = id,
            AccessTime = NfsTimestamp.FromDateTimeOffset(info.LastAccessTimeUtc),
            ModifyTime = NfsTimestamp.FromDateTimeOffset(info.LastWriteTimeUtc),
            ChangeTime = NfsTimestamp.FromDateTimeOffset(info.LastWriteTimeUtc),
        };
    }

#if NETSTANDARD2_0
    // On .NET Standard 2.0 the INfsFileSystem optional operations are abstract (no default
    // interface implementations), so the unsupported-operation defaults are provided explicitly.

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> MakeSpecialNodeAsync(
        NfsFileHandle directory,
        string name,
        NfsFileType type,
        CancellationToken cancellationToken = default) =>
        throw new NfsException(NfsStatus.NotSupported);

    /// <inheritdoc/>
    public ValueTask CreateHardLinkAsync(
        NfsFileHandle target,
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        throw new NfsException(NfsStatus.NotSupported);
#endif
}
