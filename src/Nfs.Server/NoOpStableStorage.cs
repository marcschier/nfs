namespace Nfs.Server;

/// <summary>A stable-storage implementation that intentionally keeps no records.</summary>
public sealed class NoOpStableStorage : IStableStorage
{
    /// <summary>Gets the shared no-op storage instance.</summary>
    public static NoOpStableStorage Instance { get; } = new();

    private NoOpStableStorage()
    {
    }

    /// <inheritdoc/>
    public ValueTask RecordClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask RemoveClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>> ListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>>([]);
    }
}
