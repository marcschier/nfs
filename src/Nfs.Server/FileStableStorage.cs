using System.Buffers.Binary;

namespace Nfs.Server;

/// <summary>Stores NFSv4 stable client records as one flushed binary file per client owner.</summary>
public sealed class FileStableStorage : IStableStorage
{
    private const uint Magic = 0x4E465353;
    private const ushort Version = 1;
    private const int HeaderSize = sizeof(uint) + sizeof(ushort) + sizeof(ushort);
    private const string Extension = ".client";

    private readonly string _directory;

    /// <summary>Creates file-backed stable storage under <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory that receives durable client record files.</param>
    public FileStableStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc/>
    public ValueTask RecordClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] owner = clientOwner.ToArray();
        string path = PathFor(owner);
        Directory.CreateDirectory(_directory);
        string tempPath = Path.Combine(_directory, Convert.ToHexString(Guid.NewGuid().ToByteArray()) + ".tmp");
        try
        {
            byte[] record = Encode(owner);
#if NETSTANDARD
            using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough))
#else
            using (var stream = new FileStream(
                tempPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.WriteThrough,
                    BufferSize = 4096,
                }))
#endif
            {
                stream.Write(record);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(clientOwner.Span);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>> ListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_directory))
        {
            return new ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>>([]);
        }

        var clients = new List<ReadOnlyMemory<byte>>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*" + Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] record = File.ReadAllBytes(path);
            if (TryDecode(record, out byte[]? owner))
            {
                clients.Add(owner);
            }
        }

        return new ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>>(clients);
    }

    private string PathFor(ReadOnlySpan<byte> clientOwner) =>
        Path.Combine(_directory, Convert.ToHexString(clientOwner) + Extension);

    private static byte[] Encode(ReadOnlySpan<byte> clientOwner)
    {
        if (clientOwner.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(clientOwner));
        }

        byte[] record = new byte[HeaderSize + clientOwner.Length];
        BinaryPrimitives.WriteUInt32BigEndian(record, Magic);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(sizeof(uint)), Version);
        BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(sizeof(uint) + sizeof(ushort)), (ushort)clientOwner.Length);
        clientOwner.CopyTo(record.AsSpan(HeaderSize));
        return record;
    }

    private static bool TryDecode(ReadOnlySpan<byte> record, out byte[]? clientOwner)
    {
        clientOwner = null;
        if (record.Length < HeaderSize ||
            BinaryPrimitives.ReadUInt32BigEndian(record) != Magic ||
            BinaryPrimitives.ReadUInt16BigEndian(record[sizeof(uint)..]) != Version)
        {
            return false;
        }

        int length = BinaryPrimitives.ReadUInt16BigEndian(record[(sizeof(uint) + sizeof(ushort))..]);
        if (record.Length != HeaderSize + length)
        {
            return false;
        }

        clientOwner = record.Slice(HeaderSize, length).ToArray();
        return true;
    }
}
