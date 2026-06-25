namespace Nfs.Server;

/// <summary>Persists NFSv4 client identities that hold server state across process restarts.</summary>
public interface IStableStorage
{
    /// <summary>Records that a client identity currently holds NFSv4 state.</summary>
    /// <param name="clientOwner">The opaque client owner identity.</param>
    /// <param name="cancellationToken">A token that observes cancellation.</param>
    /// <returns>A task that completes when the record is durable.</returns>
    ValueTask RecordClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default);

    /// <summary>Removes a client identity after its state is destroyed or expires.</summary>
    /// <param name="clientOwner">The opaque client owner identity.</param>
    /// <param name="cancellationToken">A token that observes cancellation.</param>
    /// <returns>A task that completes when the removal is durable.</returns>
    ValueTask RemoveClientAsync(ReadOnlyMemory<byte> clientOwner, CancellationToken cancellationToken = default);

    /// <summary>Lists client identities recorded before this server instance started.</summary>
    /// <param name="cancellationToken">A token that observes cancellation.</param>
    /// <returns>The persisted client owner identities.</returns>
    ValueTask<IReadOnlyList<ReadOnlyMemory<byte>>> ListClientsAsync(CancellationToken cancellationToken = default);
}
