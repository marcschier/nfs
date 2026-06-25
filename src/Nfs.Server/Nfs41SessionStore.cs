using System.Buffers.Binary;

using Nfs.Protocol.V4;

namespace Nfs.Server;

/// <summary>
/// A minimal in-memory store of NFS version 4.1 session state: client identifiers established by
/// EXCHANGE_ID, sessions created by CREATE_SESSION, and per-slot reply caching driven by SEQUENCE.
/// It implements exactly-once semantics within a slot but not lease expiry, trunking, or the
/// back channel.
/// </summary>
public sealed class Nfs41SessionStore
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly IStableStorage _stableStorage;
    private readonly Dictionary<string, ulong> _clientIdByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, ClientRecord> _clients = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private ulong _nextClientId;
    private ulong _nextSessionId;

    /// <summary>Creates a session store.</summary>
    /// <param name="timeProvider">The clock used to refresh v4.1 client leases.</param>
    /// <param name="stableStorage">The stable storage used to persist confirmed v4.1 clients.</param>
    public Nfs41SessionStore(TimeProvider? timeProvider = null, IStableStorage? stableStorage = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stableStorage = stableStorage ?? NoOpStableStorage.Instance;
    }

    /// <summary>The kind of outcome a SEQUENCE produces.</summary>
    public enum SequenceKind
    {
        /// <summary>Process the rest of the COMPOUND.</summary>
        Proceed,

        /// <summary>Return the cached reply for a retransmitted request.</summary>
        Cached,

        /// <summary>Fail with the given status.</summary>
        Error,
    }

    /// <summary>Establishes or refreshes a client identifier (EXCHANGE_ID).</summary>
    /// <param name="ownerId">The client's opaque owner identifier.</param>
    /// <returns>The assigned client identifier and the sequence value to use for CREATE_SESSION.</returns>
    public (ulong ClientId, uint SequenceId) ExchangeId(byte[] ownerId)
    {
        string key = Convert.ToHexString(ownerId ?? []);
        lock (_gate)
        {
            if (!_clientIdByOwner.TryGetValue(key, out ulong clientId))
            {
                clientId = ++_nextClientId;
                _clientIdByOwner[key] = clientId;
                _clients[clientId] = new ClientRecord(ownerId ?? [], false, _timeProvider.GetTimestamp());
            }

            return (clientId, 1);
        }
    }

    /// <summary>Creates a session for a client (CREATE_SESSION).</summary>
    /// <param name="clientId">The client identifier from EXCHANGE_ID.</param>
    /// <param name="slotCount">The number of fore-channel slots requested.</param>
    /// <param name="backChannelSlots">The number of back-channel slots requested.</param>
    /// <param name="flags">The CREATE_SESSION flags.</param>
    /// <param name="callbackProgram">The callback program number.</param>
    /// <param name="backChannelTransport">The transport to use for session back-channel callbacks.</param>
    /// <returns>The new session identifier, or <see langword="null"/> if the client is unknown.</returns>
    public byte[]? CreateSession(
        ulong clientId,
        uint slotCount,
        uint backChannelSlots,
        uint flags,
        uint callbackProgram,
        INfs41BackChannelTransport? backChannelTransport = null)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientId, out ClientRecord? client))
            {
                return null;
            }

            client.Confirmed = true;
            client.LastRenewed = _timeProvider.GetTimestamp();
            WaitFor(_stableStorage.RecordClientAsync(client.Owner));
            uint slots = Math.Clamp(slotCount, 1u, 256u);
            uint callbackSlots = Math.Clamp(backChannelSlots, 1u, 16u);
            bool backChannel = (flags & Nfs4CreateSessionOp.FlagConnectionBackChannel) != 0 &&
                callbackProgram != 0 &&
                backChannelTransport is not null;
            byte[] sessionId = MakeSessionId(++_nextSessionId);
            _sessions[Convert.ToHexString(sessionId)] = new Session(
                clientId,
                slots,
                backChannel ? callbackSlots : 0,
                callbackProgram,
                backChannelTransport);
            return sessionId;
        }
    }

    /// <summary>Begins a SEQUENCE: validates the session/slot and decides whether to proceed or replay.</summary>
    /// <param name="op">The SEQUENCE operation arguments.</param>
    /// <returns>The outcome kind plus the cached reply, error status, or success result as applicable.</returns>
    public (SequenceKind Kind, byte[]? Cached, Nfs4Status Status, Nfs4SequenceResult? Result) BeginSequence(
        Nfs4SequenceOp op)
    {
        lock (_gate)
        {
            string sessionKey = Convert.ToHexString(op.SessionId ?? []);
            if (!_sessions.TryGetValue(sessionKey, out Session? session))
            {
                return (SequenceKind.Error, null, Nfs4Status.BadSession, null);
            }

            if (_clients.TryGetValue(session.ClientId, out ClientRecord? client))
            {
                client.LastRenewed = _timeProvider.GetTimestamp();
            }

            if (op.SlotId >= session.Slots.Length)
            {
                return (SequenceKind.Error, null, Nfs4Status.BadSlot, null);
            }

            Slot slot = session.Slots[op.SlotId];
            if (op.SequenceId == slot.LastSequenceId && slot.CachedReply is { } cached)
            {
                return (SequenceKind.Cached, cached, Nfs4Status.Ok, null);
            }

            if (op.SequenceId != slot.LastSequenceId && op.SequenceId != slot.LastSequenceId + 1)
            {
                return (SequenceKind.Error, null, Nfs4Status.SequenceMisordered, null);
            }

            slot.LastSequenceId = op.SequenceId;
            slot.CachedReply = null;
            var result = new Nfs4SequenceResult
            {
                Status = Nfs4Status.Ok,
                SessionId = op.SessionId ?? new byte[Nfs4.SessionIdSize],
                SequenceId = op.SequenceId,
                SlotId = op.SlotId,
                HighestSlotId = (uint)(session.Slots.Length - 1),
                TargetHighestSlotId = (uint)(session.Slots.Length - 1),
                StatusFlags = 0,
            };
            return (SequenceKind.Proceed, null, Nfs4Status.Ok, result);
        }
    }

    /// <summary>Gets the owner bytes for a known client.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <returns>The client owner bytes, or <see langword="null"/> if unknown.</returns>
    public byte[]? GetClientOwner(ulong clientId)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(clientId, out ClientRecord? client) ? [.. client.Owner] : null;
        }
    }

    /// <summary>Returns whether a client has a usable session back channel.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <returns><see langword="true"/> if a back channel is available.</returns>
    public bool HasBackChannel(ulong clientId)
    {
        lock (_gate)
        {
            foreach (Session session in _sessions.Values)
            {
                if (session.ClientId == clientId && session.BackChannelSlots.Length != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the client identifier that owns a session.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The client identifier, or <see langword="null"/> if the session is unknown.</returns>
    public ulong? GetClientId(byte[] sessionId)
    {
        lock (_gate)
        {
            string sessionKey = Convert.ToHexString(sessionId ?? []);
            return _sessions.TryGetValue(sessionKey, out Session? session) ? session.ClientId : null;
        }
    }

    /// <summary>Allocates the next minimal CB_SEQUENCE state for a client's back channel.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <returns>The call state, or <see langword="null"/> if no back channel is available.</returns>
    public (Nfs41BackChannelCall Call, INfs41BackChannelTransport Transport)? NextBackChannelCall(ulong clientId)
    {
        lock (_gate)
        {
            foreach (KeyValuePair<string, Session> entry in _sessions)
            {
                Session session = entry.Value;
                if (session.ClientId != clientId ||
                    session.BackChannelSlots.Length == 0 ||
                    session.BackChannelTransport is null)
                {
                    continue;
                }

                BackChannelSlot slot = session.BackChannelSlots[0];
                slot.SequenceId++;
                var call = new Nfs41BackChannelCall(
                    clientId,
                    Convert.FromHexString(entry.Key),
                    session.CallbackProgram,
                    slot.SequenceId,
                    0,
                    (uint)(session.BackChannelSlots.Length - 1),
                    new Nfs4CallbackCompoundArgs());
                return (call, session.BackChannelTransport);
            }

            return null;
        }
    }

    /// <summary>Caches the encoded reply for a slot after a COMPOUND completes.</summary>
    /// <param name="op">The SEQUENCE operation that led the COMPOUND.</param>
    /// <param name="reply">The encoded COMPOUND reply bytes.</param>
    public void CacheReply(Nfs4SequenceOp op, byte[] reply)
    {
        lock (_gate)
        {
            string sessionKey = Convert.ToHexString(op.SessionId ?? []);
            if (_sessions.TryGetValue(sessionKey, out Session? session) && op.SlotId < session.Slots.Length)
            {
                session.Slots[op.SlotId].CachedReply = op.CacheThis ? reply : null;
            }
        }
    }

    /// <summary>Destroys a session.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns><see langword="true"/> if a session was removed.</returns>
    public bool DestroySession(byte[] sessionId)
    {
        lock (_gate)
        {
            return _sessions.Remove(Convert.ToHexString(sessionId ?? []));
        }
    }

    /// <summary>Destroys a client identifier and any owner mapping.</summary>
    /// <param name="clientId">The client identifier.</param>
    /// <returns><see langword="true"/> if the client was known.</returns>
    public bool DestroyClientId(ulong clientId)
    {
        lock (_gate)
        {
            bool known = _clients.Remove(clientId, out ClientRecord? client);
            foreach (KeyValuePair<string, ulong> entry in _clientIdByOwner)
            {
                if (entry.Value == clientId)
                {
                    _clientIdByOwner.Remove(entry.Key);
                    break;
                }
            }

            if (client is not null)
            {
                WaitFor(_stableStorage.RemoveClientAsync(client.Owner));
            }

            return known;
        }
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

    private static byte[] MakeSessionId(ulong id)
    {
        byte[] sessionId = new byte[Nfs4.SessionIdSize];
        BinaryPrimitives.WriteUInt32BigEndian(sessionId, 0x4E465334); // "NFS4"
        BinaryPrimitives.WriteUInt64BigEndian(sessionId.AsSpan(4), id);
        return sessionId;
    }

    private sealed class Session
    {
        public Session(
            ulong clientId,
            uint slots,
            uint backChannelSlots,
            uint callbackProgram,
            INfs41BackChannelTransport? backChannelTransport)
        {
            ClientId = clientId;
            CallbackProgram = callbackProgram;
            BackChannelTransport = backChannelTransport;
            Slots = new Slot[slots];
            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i] = new Slot();
            }

            BackChannelSlots = new BackChannelSlot[backChannelSlots];
            for (int i = 0; i < BackChannelSlots.Length; i++)
            {
                BackChannelSlots[i] = new BackChannelSlot();
            }
        }

        public ulong ClientId { get; }

        public uint CallbackProgram { get; }

        public INfs41BackChannelTransport? BackChannelTransport { get; }

        public Slot[] Slots { get; }

        public BackChannelSlot[] BackChannelSlots { get; }
    }

    private sealed class Slot
    {
        public uint LastSequenceId { get; set; }

        public byte[]? CachedReply { get; set; }
    }

    private sealed class BackChannelSlot
    {
        public uint SequenceId { get; set; }
    }

    private sealed class ClientRecord(byte[] owner, bool confirmed, long lastRenewed)
    {
        public byte[] Owner { get; } = owner;

        public bool Confirmed { get; set; } = confirmed;

        public long LastRenewed { get; set; } = lastRenewed;
    }
}
