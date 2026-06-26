using Nfs.Protocol.V4;

namespace Nfs.Client;

/// <summary>Tracks the NFSv4.1 SEQUENCE slot used by pNFS metadata operations.</summary>
public sealed class Nfs4PnfsSession
{
    private readonly object _gate = new();
    private readonly byte[] _sessionId;
    private readonly uint _slotId;
    private readonly uint _highestSlotId;
    private uint _nextSequenceId;

    /// <summary>Creates a pNFS metadata session sequencer.</summary>
    /// <param name="sessionId">The 16-byte NFSv4.1 session identifier.</param>
    /// <param name="nextSequenceId">The next sequence id to send on <paramref name="slotId"/>.</param>
    /// <param name="slotId">The session slot to use.</param>
    /// <param name="highestSlotId">The highest slot id the client will use.</param>
    public Nfs4PnfsSession(
        ReadOnlySpan<byte> sessionId,
        uint nextSequenceId = 1,
        uint slotId = 0,
        uint highestSlotId = 0)
    {
        if (sessionId.Length != Nfs4.SessionIdSize)
        {
            throw new ArgumentException("An NFSv4.1 session id must be 16 bytes.", nameof(sessionId));
        }

        _sessionId = sessionId.ToArray();
        _nextSequenceId = nextSequenceId;
        _slotId = slotId;
        _highestSlotId = highestSlotId;
    }

    /// <summary>Gets the session identifier.</summary>
    public ReadOnlyMemory<byte> SessionId => _sessionId;

    internal Nfs4SequenceOp NextSequence(bool cacheThis = false)
    {
        lock (_gate)
        {
            return new Nfs4SequenceOp
            {
                SessionId = _sessionId.ToArray(),
                SequenceId = _nextSequenceId++,
                SlotId = _slotId,
                HighestSlotId = _highestSlotId,
                CacheThis = cacheThis,
            };
        }
    }
}
