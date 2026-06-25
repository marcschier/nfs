using System.Buffers;
using System.Buffers.Binary;

using Nfs.Abstractions;

namespace Nfs.Server;

/// <summary>
/// A simple in-memory <see cref="INfsFileSystem"/>, useful for samples and tests. Objects are
/// addressed by a monotonically increasing identifier encoded as an eight-byte handle.
/// </summary>
public sealed class InMemoryFileSystem : INfsFileSystem
{
    private const uint DefaultDirectoryMode = 0x1ED; // 0755
    private const uint DefaultFileMode = 0x1A4;      // 0644

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private ulong _nextId;

    /// <summary>Creates an empty file system containing only a root directory.</summary>
    public InMemoryFileSystem() => Root = CreateNode(NfsFileType.Directory, DefaultDirectoryMode, content: null).Handle;

    /// <summary>Gets the handle of the root directory.</summary>
    public NfsFileHandle Root { get; }

    /// <summary>Creates a sub-directory and returns its handle.</summary>
    /// <param name="parent">The parent directory handle.</param>
    /// <param name="name">The new directory's name.</param>
    /// <returns>The handle of the new directory.</returns>
    public NfsFileHandle CreateDirectory(NfsFileHandle parent, string name)
    {
        Node directory = RequireDirectory(parent);
        Node node = CreateNode(NfsFileType.Directory, DefaultDirectoryMode, content: null);
        node.Parent = parent;
        directory.Children![name] = node.Handle;
        return node.Handle;
    }

    /// <summary>Creates a file with the given contents and returns its handle.</summary>
    /// <param name="parent">The parent directory handle.</param>
    /// <param name="name">The new file's name.</param>
    /// <param name="content">The file contents.</param>
    /// <returns>The handle of the new file.</returns>
    public NfsFileHandle CreateFile(NfsFileHandle parent, string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Node directory = RequireDirectory(parent);
        Node node = CreateNode(NfsFileType.Regular, DefaultFileMode, content);
        node.Parent = parent;
        directory.Children![name] = node.Handle;
        return node.Handle;
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> GetRootHandleAsync(CancellationToken cancellationToken = default) => new(Root);

    /// <inheritdoc/>
    public ValueTask<NfsFileAttributes> GetAttributesAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default) =>
        new(GetAttributes(Require(handle)));

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> LookupAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node node = RequireDirectory(directory);
        return node.Children!.TryGetValue(name, out NfsFileHandle child)
            ? new ValueTask<NfsFileHandle>(child)
            : throw new NfsException(NfsStatus.NoEntry);
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> LookupParentAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        Node node = Require(handle);
        return node.Parent is { } parent
            ? new ValueTask<NfsFileHandle>(parent)
            : throw new NfsException(NfsStatus.NoEntry);
    }

    /// <inheritdoc/>
    public ValueTask<NfsReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default)
    {
        Node node = RequireFile(file);
        byte[] content = node.Content ?? [];

        if (offset >= (ulong)content.Length)
        {
            return new ValueTask<NfsReadResult>(new NfsReadResult(ReadOnlyMemory<byte>.Empty, EndOfFile: true));
        }

        int start = (int)offset;
        int length = (int)Math.Min(count, (uint)(content.Length - start));
        byte[] data = content.AsSpan(start, length).ToArray();
        bool eof = start + length >= content.Length;
        return new ValueTask<NfsReadResult>(new NfsReadResult(data, eof));
    }

    /// <inheritdoc/>
    public ValueTask<NfsBufferedReadResult> ReadAsync(
        NfsFileHandle file,
        ulong offset,
        uint count,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        Node node = RequireFile(file);
        byte[] content = node.Content ?? [];

        if (offset >= (ulong)content.Length)
        {
            return new ValueTask<NfsBufferedReadResult>(new NfsBufferedReadResult(0, EndOfFile: true));
        }

        int start = (int)offset;
        int length = (int)Math.Min(count, (uint)(content.Length - start));
        destination.Write(content.AsSpan(start, length));
        bool eof = start + length >= content.Length;
        return new ValueTask<NfsBufferedReadResult>(new NfsBufferedReadResult((uint)length, eof));
    }

    /// <inheritdoc/>
    public ValueTask<NfsWriteResult> WriteAsync(
        NfsFileHandle file,
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        Node node = RequireFile(file);
        int start = (int)offset;
        int end = start + data.Length;

        byte[] content = node.Content ?? [];
        if (content.Length < end)
        {
            byte[] grown = new byte[end];
            content.CopyTo(grown.AsSpan());
            content = grown;
            node.Content = content;
        }

        data.Span.CopyTo(content.AsSpan(start));
        return new ValueTask<NfsWriteResult>(new NfsWriteResult((uint)data.Length));
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> CreateAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node parent = RequireDirectory(directory);
        if (parent.Children!.ContainsKey(name))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        Node node = CreateNode(NfsFileType.Regular, DefaultFileMode, []);
        node.Parent = directory;
        parent.Children[name] = node.Handle;
        return new ValueTask<NfsFileHandle>(node.Handle);
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> MakeDirectoryAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node parent = RequireDirectory(directory);
        if (parent.Children!.ContainsKey(name))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        Node node = CreateNode(NfsFileType.Directory, DefaultDirectoryMode, content: null);
        node.Parent = directory;
        parent.Children[name] = node.Handle;
        return new ValueTask<NfsFileHandle>(node.Handle);
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> MakeSpecialNodeAsync(
        NfsFileHandle directory,
        string name,
        NfsFileType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (type is not (NfsFileType.Fifo or NfsFileType.Socket))
        {
            throw new NfsException(NfsStatus.NotSupported);
        }

        Node parent = RequireDirectory(directory);
        if (parent.Children!.ContainsKey(name))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        Node node = CreateNode(type, 0x1A4, content: null);
        node.Parent = directory;
        parent.Children[name] = node.Handle;
        return new ValueTask<NfsFileHandle>(node.Handle);
    }

    /// <inheritdoc/>
    public ValueTask RemoveAsync(NfsFileHandle directory, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node parent = RequireDirectory(directory);
        if (!parent.Children!.TryGetValue(name, out NfsFileHandle child))
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        Node node = Require(child);
        if (node.Type == NfsFileType.Directory)
        {
            throw new NfsException(NfsStatus.IsDirectory);
        }

        parent.Children.Remove(name);
        if (--node.Links == 0)
        {
            _nodes.Remove(Key(child));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveDirectoryAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node parent = RequireDirectory(directory);
        if (!parent.Children!.TryGetValue(name, out NfsFileHandle child))
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        Node node = Require(child);
        if (node.Type != NfsFileType.Directory)
        {
            throw new NfsException(NfsStatus.NotDirectory);
        }

        if (node.Children!.Count > 0)
        {
            throw new NfsException(NfsStatus.DirectoryNotEmpty);
        }

        parent.Children.Remove(name);
        _nodes.Remove(Key(child));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsDirectoryListing> ReadDirectoryAsync(
        NfsFileHandle directory,
        ulong cookie,
        uint count,
        CancellationToken cancellationToken = default)
    {
        Node node = RequireDirectory(directory);

        var ordered = node.Children!
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        var entries = new List<NfsDirectoryEntry>();
        for (int i = (int)cookie; i < ordered.Count; i++)
        {
            ulong fileId = Require(ordered[i].Value).Id;
            entries.Add(new NfsDirectoryEntry(ordered[i].Key, fileId, (ulong)(i + 1)));
        }

        return new ValueTask<NfsDirectoryListing>(new NfsDirectoryListing(entries, EndOfStream: true));
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileAttributes> SetAttributesAsync(
        NfsFileHandle handle,
        NfsSetAttributes attributes,
        CancellationToken cancellationToken = default)
    {
        Node node = Require(handle);

        if (attributes.Size is { } size)
        {
            if (node.Type != NfsFileType.Regular)
            {
                throw new NfsException(NfsStatus.InvalidArgument);
            }

            byte[] current = node.Content ?? [];
            if ((ulong)current.Length != size)
            {
                byte[] resized = new byte[size];
                current.AsSpan(0, (int)Math.Min((ulong)current.Length, size)).CopyTo(resized);
                node.Content = resized;
            }
        }

        if (attributes.Mode is { } mode)
        {
            node.Mode = mode;
        }

        if (attributes.Uid is { } uid)
        {
            node.Uid = uid;
        }

        if (attributes.Gid is { } gid)
        {
            node.Gid = gid;
        }

        if (attributes.AccessControlList is { } acl)
        {
            node.AccessControlList = [.. acl];
        }

        if (attributes.AccessTime is { } accessTime)
        {
            node.AccessTime = accessTime;
        }

        if (attributes.ModifyTime is { } modifyTime)
        {
            node.ModifyTime = modifyTime;
        }

        node.ChangeTime = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return new ValueTask<NfsFileAttributes>(GetAttributes(node));
    }

    /// <inheritdoc/>
    public ValueTask RenameAsync(
        NfsFileHandle sourceDirectory,
        string sourceName,
        NfsFileHandle targetDirectory,
        string targetName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(targetName);
        Node source = RequireDirectory(sourceDirectory);
        Node target = RequireDirectory(targetDirectory);

        if (!source.Children!.TryGetValue(sourceName, out NfsFileHandle moving))
        {
            throw new NfsException(NfsStatus.NoEntry);
        }

        bool movingIsDirectory = Require(moving).Type == NfsFileType.Directory;

        if (target.Children!.TryGetValue(targetName, out NfsFileHandle existing))
        {
            Node existingNode = Require(existing);
            bool existingIsDirectory = existingNode.Type == NfsFileType.Directory;
            if (movingIsDirectory != existingIsDirectory)
            {
                throw new NfsException(existingIsDirectory ? NfsStatus.IsDirectory : NfsStatus.NotDirectory);
            }

            if (existingIsDirectory && existingNode.Children!.Count > 0)
            {
                throw new NfsException(NfsStatus.DirectoryNotEmpty);
            }

            if (existing != moving)
            {
                target.Children.Remove(targetName);
                if (existingIsDirectory || --existingNode.Links == 0)
                {
                    _nodes.Remove(Key(existing));
                }
            }
        }

        source.Children.Remove(sourceName);
        target.Children[targetName] = moving;
        Require(moving).Parent = targetDirectory;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileHandle> CreateSymbolicLinkAsync(
        NfsFileHandle directory,
        string name,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(target);
        Node parent = RequireDirectory(directory);
        if (parent.Children!.ContainsKey(name))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        Node node = CreateNode(NfsFileType.SymbolicLink, 0x1FF, System.Text.Encoding.UTF8.GetBytes(target));
        node.SymlinkTarget = target;
        node.Parent = directory;
        parent.Children[name] = node.Handle;
        return new ValueTask<NfsFileHandle>(node.Handle);
    }

    /// <inheritdoc/>
    public ValueTask<string> ReadSymbolicLinkAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        Node node = Require(handle);
        return node.Type == NfsFileType.SymbolicLink && node.SymlinkTarget is not null
            ? new ValueTask<string>(node.SymlinkTarget)
            : throw new NfsException(NfsStatus.InvalidArgument);
    }

    /// <inheritdoc/>
    public ValueTask CreateHardLinkAsync(
        NfsFileHandle target,
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        Node node = Require(target);
        if (node.Type == NfsFileType.Directory)
        {
            throw new NfsException(NfsStatus.IsDirectory);
        }

        Node parent = RequireDirectory(directory);
        if (parent.Children!.ContainsKey(name))
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        node.Links++;
        node.Parent ??= directory;
        parent.Children[name] = node.Handle;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<NfsAccessControlEntry>> GetAccessControlListAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        Node node = Require(handle);
        IReadOnlyList<NfsAccessControlEntry> acl = node.AccessControlList ?? NfsMetadataHelpers.AclFromMode(node.Mode);
        return new ValueTask<IReadOnlyList<NfsAccessControlEntry>>(acl);
    }

    /// <inheritdoc/>
    public ValueTask SetAccessControlListAsync(
        NfsFileHandle handle,
        IReadOnlyList<NfsAccessControlEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Node node = Require(handle);
        node.AccessControlList = [.. entries];
        node.ChangeTime = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<byte[]> GetExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
    {
        NfsMetadataHelpers.ValidateExtendedAttributeName(name);
        Node node = Require(handle);
        return node.ExtendedAttributes.TryGetValue(name, out byte[]? value)
            ? new ValueTask<byte[]>([.. value])
            : throw new NfsException(NfsStatus.NoExtendedAttribute);
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
        Node node = Require(handle);
        bool exists = node.ExtendedAttributes.ContainsKey(name);
        if (mode == NfsSetExtendedAttributeMode.Create && exists)
        {
            throw new NfsException(NfsStatus.AlreadyExists);
        }

        if (mode == NfsSetExtendedAttributeMode.Replace && !exists)
        {
            throw new NfsException(NfsStatus.NoExtendedAttribute);
        }

        if (!exists && node.ExtendedAttributes.Count >= NfsMetadataHelpers.MaxExtendedAttributesPerObject)
        {
            throw new NfsException(NfsStatus.ExtendedAttributeTooBig);
        }

        node.ExtendedAttributes[name] = value.ToArray();
        node.ChangeTime = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsExtendedAttributeListing> ListExtendedAttributesAsync(
        NfsFileHandle handle,
        ulong cookie,
        uint maxCount,
        CancellationToken cancellationToken = default)
    {
        Node node = Require(handle);
        return new ValueTask<NfsExtendedAttributeListing>(
            NfsMetadataHelpers.ListExtendedAttributes(node.ExtendedAttributes.Keys, cookie, maxCount));
    }

    /// <inheritdoc/>
    public ValueTask RemoveExtendedAttributeAsync(
        NfsFileHandle handle,
        string name,
        CancellationToken cancellationToken = default)
    {
        NfsMetadataHelpers.ValidateExtendedAttributeName(name);
        Node node = Require(handle);
        if (!node.ExtendedAttributes.Remove(name))
        {
            throw new NfsException(NfsStatus.NoExtendedAttribute);
        }

        node.ChangeTime = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<NfsFileSystemStats> GetFileSystemStatsAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        _ = Require(handle);
        ulong used = 0;
        foreach (Node node in _nodes.Values)
        {
            used += (ulong)(node.Content?.Length ?? 0);
        }

        const ulong capacity = 1UL << 40; // a nominal 1 TiB capacity
        return new ValueTask<NfsFileSystemStats>(new NfsFileSystemStats
        {
            TotalBytes = capacity,
            FreeBytes = capacity - Math.Min(used, capacity),
            AvailableBytes = capacity - Math.Min(used, capacity),
            TotalFiles = uint.MaxValue,
            FreeFiles = uint.MaxValue - (ulong)_nodes.Count,
            AvailableFiles = uint.MaxValue - (ulong)_nodes.Count,
            InvariantSeconds = 0,
        });
    }

    private Node CreateNode(NfsFileType type, uint mode, byte[]? content)
    {
        ulong id = ++_nextId;
        NfsFileHandle handle = MakeHandle(id);
        NfsTimestamp now = NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        var node = new Node
        {
            Handle = handle,
            Id = id,
            Type = type,
            Mode = mode,
            Content = content,
            AccessTime = now,
            ModifyTime = now,
            ChangeTime = now,
            Children = type == NfsFileType.Directory
                ? new Dictionary<string, NfsFileHandle>(StringComparer.Ordinal)
                : null,
        };
        _nodes[Key(handle)] = node;
        return node;
    }

    private Node Require(NfsFileHandle handle) =>
        _nodes.TryGetValue(Key(handle), out Node? node) ? node : throw new NfsException(NfsStatus.StaleHandle);

    private Node RequireDirectory(NfsFileHandle handle)
    {
        Node node = Require(handle);
        return node.Type == NfsFileType.Directory ? node : throw new NfsException(NfsStatus.NotDirectory);
    }

    private Node RequireFile(NfsFileHandle handle)
    {
        Node node = Require(handle);
        return node.Type == NfsFileType.Regular ? node : throw new NfsException(NfsStatus.IsDirectory);
    }

    private static NfsFileAttributes GetAttributes(Node node) => new()
    {
        Type = node.Type,
        Mode = node.Mode,
        LinkCount = node.Type == NfsFileType.Directory ? 2u : node.Links,
        Uid = node.Uid,
        Gid = node.Gid,
        Size = (ulong)(node.Content?.Length ?? 0),
        Used = (ulong)(node.Content?.Length ?? 0),
        FileId = node.Id,
        AccessTime = node.AccessTime,
        ModifyTime = node.ModifyTime,
        ChangeTime = node.ChangeTime,
    };

    private static NfsFileHandle MakeHandle(ulong id)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, id);
        return new NfsFileHandle(bytes);
    }

    private static string Key(NfsFileHandle handle) => Convert.ToHexString(handle.Span);

    private sealed class Node
    {
        public NfsFileHandle Handle { get; init; }

        public NfsFileHandle? Parent { get; set; }

        public ulong Id { get; init; }

        public NfsFileType Type { get; init; }

        public uint Mode { get; set; }

        public uint Uid { get; set; }

        public uint Gid { get; set; }

        public uint Links { get; set; } = 1;

        public byte[]? Content { get; set; }

        public string? SymlinkTarget { get; set; }

        public NfsTimestamp AccessTime { get; set; }

        public NfsTimestamp ModifyTime { get; set; }

        public NfsTimestamp ChangeTime { get; set; }

        public Dictionary<string, NfsFileHandle>? Children { get; init; }

        public IReadOnlyList<NfsAccessControlEntry>? AccessControlList { get; set; }

        public Dictionary<string, byte[]> ExtendedAttributes { get; } = new(StringComparer.Ordinal);
    }
}
