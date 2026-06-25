namespace Nfs.Server;

/// <summary>An in-memory stable-storage implementation useful for tests and embedded hosts.</summary>
public sealed class InMemoryStableStorage : IStableStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _clients = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ValueTask RecordClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] owner = clientOwner.ToArray();
        lock (_gate)
        {
            _clients[Convert.ToHexString(owner)] = owner;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _clients.Remove(Convert.ToHexString(clientOwner.Span));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>> ListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var clients = new ReadOnlyMemory<byte>[_clients.Count];
            int index = 0;
            foreach (byte[] owner in _clients.Values)
            {
                clients[index++] = owner.ToArray();
            }

            return ValueTask.FromResult<IReadOnlyList<ReadOnlyMemory<byte>>>(clients);
        }
    }
}
