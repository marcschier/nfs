using System.Buffers.Binary;
using System.Security.Cryptography;

using Nfs.Abstractions;
using Nfs.Protocol.V4;

namespace Nfs.Server;

/// <summary>
/// A minimal in-memory store of NFS version 4 server state: client identifiers and open state
/// identifiers. It supports the SETCLIENTID/OPEN/CLOSE flow for a single-server, single-process
/// deployment. It tracks leases, reboot grace, open state, byte-range locks, and read delegations.
/// </summary>
public sealed class Nfs4StateStore
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly IStableStorage _stableStorage;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _graceDuration;
    private readonly TimeSpan _delegationRecallTimeout;
    private readonly Dictionary<string, ClientRecord> _clientsById = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, ClientRecord> _clientsByHandle = new();
    private readonly HashSet<string> _reclaimableClientOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpenState> _opens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LockState> _locks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DelegationState> _delegations = new(StringComparer.Ordinal);
    private readonly List<LockWaiter> _lockWaiters = [];
    private readonly long _graceStarted;
    private bool _graceComplete;
    private ulong _nextClientId;
    private ulong _nextStateId;

    /// <summary>Creates an in-memory state store.</summary>
    /// <param name="timeProvider">The clock used for lease and grace calculations.</param>
    /// <param name="leaseDuration">The lease duration, or <see langword="null"/> for the NFSv4 lease attribute.</param>
    /// <param name="graceDuration">The reboot grace duration, or <see langword="null"/> for the lease duration.</param>
    /// <param name="delegationRecallTimeout">The time to wait for DELEGRETURN before revoking a recalled delegation.</param>
    /// <param name="stableStorage">The storage used to persist client owners across restarts.</param>
    public Nfs4StateStore(
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null,
        TimeSpan? graceDuration = null,
        TimeSpan? delegationRecallTimeout = null,
        IStableStorage? stableStorage = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stableStorage = stableStorage ?? NoOpStableStorage.Instance;
        _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(Nfs4Mapping.LeaseTimeSeconds);
        _graceDuration = graceDuration ?? _leaseDuration;
        _delegationRecallTimeout = delegationRecallTimeout ?? TimeSpan.FromSeconds(1);
        _graceStarted = _timeProvider.GetTimestamp();
        foreach (ReadOnlyMemory<byte> clientOwner in WaitFor(_stableStorage.ListClientsAsync()))
        {
            _reclaimableClientOwners.Add(Convert.ToHexString(clientOwner.Span));
        }
    }

    /// <summary>Gets whether the reboot grace period is currently active.</summary>
    public bool IsInGrace
    {
        get
        {
            lock (_gate)
            {
                return IsInGraceCore();
            }
        }
    }

    /// <summary>Registers (or re-registers) a client and returns its handle and confirm verifier.</summary>
    /// <param name="verifier">The client's boot verifier.</param>
    /// <param name="id">The client's opaque identity string.</param>
    /// <param name="callback">The client's callback parameters, or <see langword="null"/> when no callback is available.</param>
    /// <returns>The assigned client identifier and the confirm verifier to echo back.</returns>
    public (ulong ClientId, byte[] Confirm) RegisterClient(
        byte[] verifier,
        byte[] id,
        Nfs4ClientCallbackInfo? callback = null)
    {
        string key = Convert.ToHexString(id ?? []);
        lock (_gate)
        {
            ulong clientId = ++_nextClientId;
            byte[] confirm = RandomNumberGenerator.GetBytes(Nfs4.VerifierSize);
            if (_clientsById.TryGetValue(key, out ClientRecord? oldRecord))
            {
                _clientsByHandle.Remove(oldRecord.ClientId);
            }

            var record = new ClientRecord(clientId, id ?? [], verifier ?? [], confirm, _timeProvider.GetTimestamp())
            {
                Callback = callback,
            };
            _clientsById[key] = record;
            _clientsByHandle[clientId] = record;
            return (clientId, confirm);
        }
    }

    /// <summary>Confirms a previously registered client.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <param name="confirm">The confirm verifier from SETCLIENTID.</param>
    /// <returns><see langword="true"/> if confirmed; otherwise <see langword="false"/>.</returns>
    public bool ConfirmClient(ulong clientId, byte[] confirm)
    {
        lock (_gate)
        {
            if (!_clientsByHandle.TryGetValue(clientId, out ClientRecord? record) ||
                !record.Confirm.AsSpan().SequenceEqual(confirm ?? []))
            {
                return false;
            }

            record.Confirmed = true;
            record.LastRenewed = _timeProvider.GetTimestamp();
            WaitFor(_stableStorage.RecordClientAsync(record.Owner));
            return true;
        }
    }

    /// <summary>Registers or confirms a v4.1 session client in the shared state table.</summary>
    /// <param name="clientId">The v4.1 client identifier.</param>
    /// <param name="owner">The client owner bytes.</param>
    public void RegisterSessionClient(ulong clientId, byte[] owner)
    {
        string key = Convert.ToHexString(owner ?? []);
        lock (_gate)
        {
            if (_clientsById.TryGetValue(key, out ClientRecord? oldRecord))
            {
                _clientsByHandle.Remove(oldRecord.ClientId);
            }

            byte[] verifier = new byte[Nfs4.VerifierSize];
            byte[] confirm = new byte[Nfs4.VerifierSize];
            var record = new ClientRecord(clientId, owner ?? [], verifier, confirm, _timeProvider.GetTimestamp())
            {
                Confirmed = true,
            };
            _clientsById[key] = record;
            _clientsByHandle[clientId] = record;
            _nextClientId = Math.Max(_nextClientId, clientId);
            WaitFor(_stableStorage.RecordClientAsync(record.Owner));
        }
    }

    /// <summary>Checks whether a reclaim operation is valid for the client and current grace state.</summary>
    /// <param name="clientId">The client identifier attempting reclaim.</param>
    /// <returns>The NFSv4 status that should gate the reclaim operation.</returns>
    public Nfs4Status CheckReclaim(ulong clientId)
    {
        lock (_gate)
        {
            if (!IsInGraceCore())
            {
                return Nfs4Status.NoGrace;
            }

            if (!_clientsByHandle.TryGetValue(clientId, out ClientRecord? record) || !record.Confirmed)
            {
                return Nfs4Status.StaleClientId;
            }

            return _reclaimableClientOwners.Contains(record.OwnerKey) ? Nfs4Status.Ok : Nfs4Status.ReclaimBad;
        }
    }

    /// <summary>Renews a known and confirmed client identifier.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <returns><see langword="true"/> if the lease was renewed.</returns>
    public bool Renew(ulong clientId)
    {
        lock (_gate)
        {
            ExpireLeasesCore();
            if (!_clientsByHandle.TryGetValue(clientId, out ClientRecord? record) || !record.Confirmed)
            {
                return false;
            }

            record.LastRenewed = _timeProvider.GetTimestamp();
            return true;
        }
    }

    /// <summary>Opens a file for a client and allocates an open state identifier.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="clientId">The owning client identifier.</param>
    /// <param name="shareAccess">The requested share access.</param>
    /// <returns>The new open state identifier.</returns>
    /// <exception cref="NfsException">The client is not confirmed.</exception>
    public (Nfs4StateId OpenStateId, Nfs4StateId? DelegationStateId, uint DelegationType) Open(
        NfsFileHandle file,
        ulong clientId,
        uint shareAccess)
    {
        lock (_gate)
        {
            ExpireLeasesCore();
            if (!_clientsByHandle.TryGetValue(clientId, out ClientRecord? record) || !record.Confirmed)
            {
                throw new NfsException(NfsStatus.StaleHandle); // maps to NFS4ERR_STALE_CLIENTID at the caller
            }

            record.LastRenewed = _timeProvider.GetTimestamp();
            string fileKey = Convert.ToHexString(file.Span);
            uint delegationType = GetGrantableDelegationType(fileKey, shareAccess);
            byte[] other = MakeOther(++_nextStateId);
            var stateId = new Nfs4StateId { Sequence = 1, Other = other };
            _opens[Convert.ToHexString(other)] = new OpenState(file, clientId, shareAccess);
            Nfs4StateId? delegationStateId = null;
            if (delegationType != Nfs4OpenResult.DelegationNone)
            {
                byte[] delegationOther = MakeOther(++_nextStateId);
                delegationStateId = new Nfs4StateId { Sequence = 1, Other = delegationOther };
                _delegations[Convert.ToHexString(delegationOther)] = new DelegationState(
                    fileKey,
                    clientId,
                    file,
                    delegationStateId.Value,
                    delegationType);
            }

            return (stateId, delegationStateId, delegationType);
        }
    }

    /// <summary>
    /// Finds a conflicting delegation and starts or observes a CB_RECALL. A recall in flight makes
    /// the caller return NFS4ERR_DELAY; once the timeout elapses the delegation is revoked.
    /// </summary>
    /// <param name="file">The file being opened or modified.</param>
    /// <param name="requesterClientId">The requesting client identifier, or 0 when unknown.</param>
    /// <param name="writeAccess">Whether the operation may write or otherwise modify the file.</param>
    /// <param name="hasAlternateCallback">Returns whether the delegated client has a non-v4.0 callback path.</param>
    /// <returns>The recall to send or wait for; <see langword="null"/> if no delegation blocks the operation.</returns>
    public Nfs4DelegationRecall? PrepareDelegationRecall(
        NfsFileHandle file,
        ulong requesterClientId,
        bool writeAccess,
        Func<ulong, bool>? hasAlternateCallback = null)
    {
        lock (_gate)
        {
            ExpireLeasesCore();
            string fileKey = Convert.ToHexString(file.Span);
            string? delegationKey = null;
            DelegationState? state = null;
            foreach (KeyValuePair<string, DelegationState> entry in _delegations)
            {
                if (entry.Value.FileKey != fileKey ||
                    entry.Value.ClientId == requesterClientId ||
                    !DelegationConflicts(entry.Value.Type, writeAccess))
                {
                    continue;
                }

                delegationKey = entry.Key;
                state = entry.Value;
                break;
            }

            if (state is null)
            {
                return null;
            }

            if (!_clientsByHandle.TryGetValue(state.ClientId, out ClientRecord? client))
            {
                _delegations.Remove(delegationKey!);
                return null;
            }

            Nfs4ClientCallbackInfo? callback = client.Callback;
            if (callback is null && hasAlternateCallback?.Invoke(state.ClientId) != true)
            {
                _delegations.Remove(delegationKey!);
                return null;
            }

            if (state.RecallStartedAt is { } startedAt &&
                _timeProvider.GetElapsedTime(startedAt) >= _delegationRecallTimeout)
            {
                _delegations.Remove(delegationKey!);
                return null;
            }

            bool notify = state.RecallStartedAt is null;
            state.RecallStartedAt ??= _timeProvider.GetTimestamp();
            return new Nfs4DelegationRecall(state.ClientId, callback, state.StateId, state.File, notify);
        }
    }

    /// <summary>Revokes a delegation without waiting for DELEGRETURN.</summary>
    /// <param name="stateId">The delegation state identifier.</param>
    public void RevokeDelegation(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            _delegations.Remove(Convert.ToHexString(stateId.Other ?? []));
        }
    }

    /// <summary>Gets the client that owns an open state identifier.</summary>
    /// <param name="stateId">The open state identifier.</param>
    /// <returns>The owning client identifier, or 0 when the state identifier is not an open.</returns>
    public ulong GetOpenClientId(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            string key = Convert.ToHexString(stateId.Other ?? []);
            return _opens.TryGetValue(key, out OpenState? state) ? state.ClientId : 0;
        }
    }

    /// <summary>Gets the client that owns any known state identifier.</summary>
    /// <param name="stateId">The state identifier.</param>
    /// <returns>The owning client identifier, or 0 when the state identifier is unknown or anonymous.</returns>
    public ulong GetStateClientId(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            string key = Convert.ToHexString(stateId.Other ?? []);
            if (_opens.TryGetValue(key, out OpenState? open))
            {
                return open.ClientId;
            }

            if (_delegations.TryGetValue(key, out DelegationState? delegation))
            {
                return delegation.ClientId;
            }

            return _locks.TryGetValue(key, out LockState? lockState) ? lockState.Owner.ClientId : 0;
        }
    }

    /// <summary>Advances an open's sequence (OPEN_CONFIRM).</summary>
    /// <param name="stateId">The open state identifier.</param>
    /// <returns>The updated state identifier.</returns>
    /// <exception cref="NfsException">The state identifier is unknown.</exception>
    public Nfs4StateId ConfirmOpen(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            OpenState state = Require(stateId);
            RenewCore(state.ClientId);
            state.Sequence++;
            return new Nfs4StateId { Sequence = state.Sequence, Other = stateId.Other ?? [] };
        }
    }

    /// <summary>Closes an open and advances its sequence.</summary>
    /// <param name="stateId">The open state identifier.</param>
    /// <returns>The final state identifier.</returns>
    /// <exception cref="NfsException">The state identifier is unknown.</exception>
    public Nfs4StateId Close(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            OpenState state = Require(stateId);
            RenewCore(state.ClientId);
            string key = Convert.ToHexString(stateId.Other ?? []);
            _opens.Remove(key);
            return new Nfs4StateId { Sequence = state.Sequence + 1, Other = stateId.Other ?? [] };
        }
    }

    private OpenState Require(Nfs4StateId stateId)
    {
        string key = Convert.ToHexString(stateId.Other ?? []);
        return _opens.TryGetValue(key, out OpenState? state) ? state : throw new NfsException(NfsStatus.BadHandle);
    }

    /// <summary>Acquires (or extends) a byte-range lock, or reports the conflicting lock.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="owner">The lock-owner.</param>
    /// <param name="write">Whether an exclusive (write) lock is requested.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The length (<c>0xFFFFFFFFFFFFFFFF</c> means to end of file).</param>
    /// <param name="existing">An existing lock state identifier, or <see langword="null"/> for a new lock-owner.</param>
    /// <returns>The granted lock state identifier, or the conflicting lock when denied.</returns>
    public (Nfs4StateId StateId, Nfs4LockDenied? Denied) Lock(
        NfsFileHandle file,
        Nfs4LockOwner owner,
        bool write,
        ulong offset,
        ulong length,
        Nfs4StateId? existing)
    {
        lock (_gate)
        {
            ExpireLeasesCore();
            string fileKey = Convert.ToHexString(file.Span);

            LockState? state = null;
            string ownerKey;
            Nfs4LockOwner effectiveOwner;
            if (existing is { } existingId)
            {
                state = RequireLock(existingId);
                ownerKey = state.OwnerKey;
                effectiveOwner = state.Owner;
                RenewCore(state.Owner.ClientId);
            }
            else
            {
                ownerKey = OwnerKey(owner);
                effectiveOwner = owner;
                RenewCore(owner.ClientId);
            }

            if (FindConflict(fileKey, ownerKey, write, offset, length) is { } conflict)
            {
                RememberLockWaiter(fileKey, file, effectiveOwner, write, offset, length);
                return (default, conflict);
            }

            if (state is null)
            {
                byte[] other = MakeOther(++_nextStateId);
                state = new LockState(fileKey, ownerKey, effectiveOwner, other);
                _locks[Convert.ToHexString(other)] = state;
            }

            state.Ranges.Add(new LockRange(offset, length, write));
            state.Sequence++;
            return (new Nfs4StateId { Sequence = state.Sequence, Other = state.Other }, null);
        }
    }

    /// <summary>Tests whether a byte-range lock could be acquired without granting it.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="owner">The prospective lock-owner.</param>
    /// <param name="write">Whether an exclusive (write) lock is tested.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The length.</param>
    /// <returns>The conflicting lock, or <see langword="null"/> if the lock would be granted.</returns>
    public Nfs4LockDenied? TestLock(NfsFileHandle file, Nfs4LockOwner owner, bool write, ulong offset, ulong length)
    {
        lock (_gate)
        {
            return FindConflict(Convert.ToHexString(file.Span), OwnerKey(owner), write, offset, length);
        }
    }

    /// <summary>Releases the locks covered by a range and advances the lock state sequence.</summary>
    /// <param name="lockStateId">The lock state identifier.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="length">The length.</param>
    /// <returns>The updated lock state identifier.</returns>
    /// <exception cref="NfsException">The state identifier is unknown.</exception>
    public (Nfs4StateId StateId, IReadOnlyList<Nfs4LockNotification> Notifications) Unlock(
        Nfs4StateId lockStateId,
        ulong offset,
        ulong length)
    {
        lock (_gate)
        {
            LockState state = RequireLock(lockStateId);
            RenewCore(state.Owner.ClientId);
            state.Ranges.RemoveAll(range => Overlaps(offset, length, range.Offset, range.Length));
            state.Sequence++;
            var stateId = new Nfs4StateId { Sequence = state.Sequence, Other = lockStateId.Other ?? [] };
            return (stateId, CollectSatisfiedLockWaiters(state.FileKey));
        }
    }

    /// <summary>Returns a delegation state identifier to the server.</summary>
    /// <param name="stateId">The delegation state identifier.</param>
    /// <returns><see langword="true"/> if a delegation was released.</returns>
    public bool ReturnDelegation(Nfs4StateId stateId)
    {
        lock (_gate)
        {
            string key = Convert.ToHexString(stateId.Other ?? []);
            if (!_delegations.TryGetValue(key, out DelegationState? state))
            {
                return false;
            }

            RenewCore(state.ClientId);
            return _delegations.Remove(key);
        }
    }

    /// <summary>Expires clients whose leases have elapsed, dropping their opens, locks, and delegations.</summary>
    /// <returns>The number of expired clients.</returns>
    public int ExpireLeases()
    {
        lock (_gate)
        {
            return ExpireLeasesCore();
        }
    }

    /// <summary>Ends reboot grace for this single-process server.</summary>
    public void CompleteReclaim()
    {
        lock (_gate)
        {
            _graceComplete = true;
        }
    }

    private Nfs4LockDenied? FindConflict(string fileKey, string ownerKey, bool write, ulong offset, ulong length)
    {
        foreach (LockState state in _locks.Values)
        {
            if (state.FileKey != fileKey || state.OwnerKey == ownerKey)
            {
                continue;
            }

            foreach (LockRange range in state.Ranges)
            {
                if ((write || range.Write) && Overlaps(offset, length, range.Offset, range.Length))
                {
                    return new Nfs4LockDenied
                    {
                        Offset = range.Offset,
                        Length = range.Length,
                        LockType = range.Write ? Nfs4LockType.Write : Nfs4LockType.Read,
                        Owner = state.Owner,
                    };
                }
            }
        }

        return null;
    }

    private LockState RequireLock(Nfs4StateId stateId)
    {
        string key = Convert.ToHexString(stateId.Other ?? []);
        return _locks.TryGetValue(key, out LockState? state) ? state : throw new NfsException(NfsStatus.BadHandle);
    }

    private bool IsInGraceCore() =>
        !_graceComplete && _timeProvider.GetElapsedTime(_graceStarted) < _graceDuration;

    private int ExpireLeasesCore()
    {
        List<ulong>? expired = null;
        foreach (ClientRecord record in _clientsByHandle.Values)
        {
            if (_timeProvider.GetElapsedTime(record.LastRenewed) >= _leaseDuration)
            {
                expired ??= [];
                expired.Add(record.ClientId);
            }
        }

        if (expired is null)
        {
            return 0;
        }

        foreach (ulong clientId in expired)
        {
            RemoveClient(clientId);
        }

        return expired.Count;
    }

    private void RemoveClient(ulong clientId)
    {
        if (!_clientsByHandle.Remove(clientId, out ClientRecord? record))
        {
            return;
        }

        string? idKey = null;
        foreach (KeyValuePair<string, ClientRecord> entry in _clientsById)
        {
            if (entry.Value.ClientId == clientId)
            {
                idKey = entry.Key;
                break;
            }
        }

        if (idKey is not null)
        {
            _clientsById.Remove(idKey);
        }

        RemoveWhere(_opens, state => state.ClientId == clientId);
        RemoveWhere(_locks, state => state.Owner.ClientId == clientId);
        RemoveWhere(_delegations, state => state.ClientId == clientId);
        _lockWaiters.RemoveAll(waiter => waiter.Owner.ClientId == clientId);
        WaitFor(_stableStorage.RemoveClientAsync(record.Owner));
    }

    private void RenewCore(ulong clientId)
    {
        if (_clientsByHandle.TryGetValue(clientId, out ClientRecord? record))
        {
            record.LastRenewed = _timeProvider.GetTimestamp();
        }
    }

    private bool HasOpenForFile(string fileKey)
    {
        foreach (OpenState state in _opens.Values)
        {
            if (Convert.ToHexString(state.File.Span) == fileKey)
            {
                return true;
            }
        }

        return false;
    }

    private uint GetGrantableDelegationType(string fileKey, uint shareAccess)
    {
        if (HasOpenForFile(fileKey) || HasLocksForFile(fileKey) || HasDelegationForFile(fileKey))
        {
            return Nfs4OpenResult.DelegationNone;
        }

        if ((shareAccess & Nfs4ShareAccess.Write) != 0)
        {
            return Nfs4OpenResult.DelegationWrite;
        }

        return shareAccess == Nfs4ShareAccess.Read
            ? Nfs4OpenResult.DelegationRead
            : Nfs4OpenResult.DelegationNone;
    }

    private bool HasLocksForFile(string fileKey)
    {
        foreach (LockState state in _locks.Values)
        {
            if (state.FileKey == fileKey && state.Ranges.Count != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasDelegationForFile(string fileKey)
    {
        foreach (DelegationState state in _delegations.Values)
        {
            if (state.FileKey == fileKey)
            {
                return true;
            }
        }

        return false;
    }

    private void RememberLockWaiter(
        string fileKey,
        NfsFileHandle file,
        Nfs4LockOwner owner,
        bool write,
        ulong offset,
        ulong length)
    {
        for (int i = 0; i < _lockWaiters.Count; i++)
        {
            LockWaiter waiter = _lockWaiters[i];
            if (waiter.FileKey == fileKey && OwnerKey(waiter.Owner) == OwnerKey(owner))
            {
                _lockWaiters[i] = new LockWaiter(fileKey, file, owner, write, offset, length);
                return;
            }
        }

        _lockWaiters.Add(new LockWaiter(fileKey, file, owner, write, offset, length));
    }

    private List<Nfs4LockNotification> CollectSatisfiedLockWaiters(string fileKey)
    {
        List<Nfs4LockNotification>? notifications = null;
        for (int i = _lockWaiters.Count - 1; i >= 0; i--)
        {
            LockWaiter waiter = _lockWaiters[i];
            if (waiter.FileKey != fileKey ||
                FindConflict(fileKey, OwnerKey(waiter.Owner), waiter.Write, waiter.Offset, waiter.Length) is not null)
            {
                continue;
            }

            notifications ??= [];
            notifications.Add(new Nfs4LockNotification(waiter.Owner.ClientId, waiter.File, waiter.Owner));
            _lockWaiters.RemoveAt(i);
        }

        return notifications ?? [];
    }

    private static bool DelegationConflicts(uint delegationType, bool writeAccess) =>
        delegationType == Nfs4OpenResult.DelegationWrite ||
        (writeAccess && delegationType == Nfs4OpenResult.DelegationRead);

    private static void RemoveWhere<T>(Dictionary<string, T> states, Func<T, bool> predicate)
    {
        List<string>? keys = null;
        foreach (KeyValuePair<string, T> entry in states)
        {
            if (predicate(entry.Value))
            {
                keys ??= [];
                keys.Add(entry.Key);
            }
        }

        if (keys is null)
        {
            return;
        }

        foreach (string key in keys)
        {
            states.Remove(key);
        }
    }

    private static bool Overlaps(ulong offsetA, ulong lengthA, ulong offsetB, ulong lengthB)
    {
        ulong endA = End(offsetA, lengthA);
        ulong endB = End(offsetB, lengthB);
        return offsetA < endB && offsetB < endA;
    }

    private static ulong End(ulong offset, ulong length)
    {
        if (length == ulong.MaxValue || offset + length < offset)
        {
            return ulong.MaxValue; // A length of all-ones, or an overflow, means "to the end of the file".
        }

        return offset + length;
    }

    private static string OwnerKey(Nfs4LockOwner owner) =>
        owner.ClientId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) +
        Convert.ToHexString(owner.Owner ?? []);

    private static byte[] MakeOther(ulong id)
    {
        byte[] other = new byte[Nfs4.OtherSize];
        BinaryPrimitives.WriteUInt32BigEndian(other, 0x4E465334); // "NFS4"
        BinaryPrimitives.WriteUInt64BigEndian(other.AsSpan(4), id);
        return other;
    }

    private static void WaitFor(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
        {
            task.GetAwaiter().GetResult();
            return;
        }

        task.AsTask().GetAwaiter().GetResult();
    }

    private static T WaitFor<T>(ValueTask<T> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return task.GetAwaiter().GetResult();
        }

        return task.AsTask().GetAwaiter().GetResult();
    }

    private sealed class ClientRecord(ulong clientId, byte[] owner, byte[] verifier, byte[] confirm, long lastRenewed)
    {
        public ulong ClientId { get; } = clientId;

        public byte[] Owner { get; } = owner;

        public string OwnerKey { get; } = Convert.ToHexString(owner);

        public byte[] Verifier { get; } = verifier;

        public byte[] Confirm { get; } = confirm;

        public bool Confirmed { get; set; }

        public long LastRenewed { get; set; } = lastRenewed;

        public Nfs4ClientCallbackInfo? Callback { get; set; }
    }

    private sealed class OpenState(NfsFileHandle file, ulong clientId, uint shareAccess)
    {
        public NfsFileHandle File { get; } = file;

        public ulong ClientId { get; } = clientId;

        public uint ShareAccess { get; } = shareAccess;

        public uint Sequence { get; set; } = 1;
    }

    private sealed class DelegationState(
        string fileKey,
        ulong clientId,
        NfsFileHandle file,
        Nfs4StateId stateId,
        uint type)
    {
        public string FileKey { get; } = fileKey;

        public ulong ClientId { get; } = clientId;

        public NfsFileHandle File { get; } = file;

        public Nfs4StateId StateId { get; } = stateId;

        public uint Type { get; } = type;

        public long? RecallStartedAt { get; set; }
    }

    private readonly record struct LockRange(ulong Offset, ulong Length, bool Write);

    private readonly record struct LockWaiter(
        string FileKey,
        NfsFileHandle File,
        Nfs4LockOwner Owner,
        bool Write,
        ulong Offset,
        ulong Length);

    private sealed class LockState(string fileKey, string ownerKey, Nfs4LockOwner owner, byte[] other)
    {
        public string FileKey { get; } = fileKey;

        public string OwnerKey { get; } = ownerKey;

        public Nfs4LockOwner Owner { get; } = owner;

        public byte[] Other { get; } = other;

        public uint Sequence { get; set; }

        public List<LockRange> Ranges { get; } = [];
    }
}

/// <summary>The callback parameters supplied by an NFSv4.0 client in SETCLIENTID.</summary>
public readonly record struct Nfs4ClientCallbackInfo(
    uint Program,
    string NetId,
    string Address,
    uint Ident);

/// <summary>A read delegation that blocks a conflicting operation and may need CB_RECALL.</summary>
public readonly record struct Nfs4DelegationRecall(
    ulong ClientId,
    Nfs4ClientCallbackInfo? Callback,
    Nfs4StateId StateId,
    NfsFileHandle File,
    bool Notify);

/// <summary>A v4.1 client to notify that a denied lock may now be available.</summary>
public readonly record struct Nfs4LockNotification(
    ulong ClientId,
    NfsFileHandle File,
    Nfs4LockOwner Owner);
