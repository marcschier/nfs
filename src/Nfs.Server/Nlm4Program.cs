using System.Buffers;
using System.Net;

using Nfs.Nlm;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the Network Lock Manager program (NLM, 100021,
/// version 4). It maintains an in-memory table of advisory byte-range locks keyed by file handle
/// and performs conflict detection. Conflicting blocking LOCK calls are queued and completed later
/// with an NLM_GRANTED callback when unlock or recovery makes the requested range available.
/// </summary>
public sealed class Nlm4Program : IRpcProgram
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Held>> _locks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<Pending>> _pending = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public uint Program => Nlm4.Program;

    /// <inheritdoc/>
    public async ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Nlm4.ProtocolVersion)
        {
            return RpcReplyPayload.ProgramMismatch(Nlm4.ProtocolVersion, Nlm4.ProtocolVersion);
        }

        return (Nlm4Procedure)request.Procedure switch
        {
            Nlm4Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nlm4Procedure.Test => Test(arguments),
            Nlm4Procedure.Lock => await LockAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nlm4Procedure.Unlock => await UnlockAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nlm4Procedure.Cancel => await CancelAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };
    }

    /// <summary>Drops all held locks owned by <paramref name="callerName"/> and grants newly-unblocked waiters.</summary>
    /// <param name="callerName">The rebooted host's caller name.</param>
    public void DropLocksForHost(string callerName)
    {
        ArgumentNullException.ThrowIfNull(callerName);
        List<Pending> grants = [];
        lock (_gate)
        {
            foreach (string key in _locks.Keys.ToArray())
            {
                List<Held> held = _locks[key];
                held.RemoveAll(h => HostMatches(h.CallerName, callerName));
                if (held.Count == 0)
                {
                    _locks.Remove(key);
                }

                ProcessWaiters(key, grants);
            }
        }

        _ = NotifyGrantedAsync(grants, CancellationToken.None);
    }

    private RpcReplyPayload Test(ReadOnlyMemory<byte> arguments)
    {
        Nlm4TestArgs args = Decode<Nlm4TestArgs>(arguments);
        lock (_gate)
        {
            Held? conflict = FindConflict(args.Lock, args.Exclusive);
            Nlm4TestRes result = conflict is { } held
                ? new Nlm4TestRes
                {
                    Cookie = args.Cookie,
                    Status = Nlm4Status.Denied,
                    Holder = new Nlm4Holder
                    {
                        Exclusive = held.Exclusive,
                        ServerId = held.ServerId,
                        Owner = held.Owner,
                        Offset = held.Offset,
                        Length = held.Length,
                    },
                }
                : new Nlm4TestRes { Cookie = args.Cookie, Status = Nlm4Status.Granted };
            return Encode(result);
        }
    }

    private async ValueTask<RpcReplyPayload> LockAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nlm4LockArgs args = Decode<Nlm4LockArgs>(arguments);
        List<Pending> grants = [];
        RpcReplyPayload reply;
        lock (_gate)
        {
            if (FindConflict(args.Lock, args.Exclusive) is not null)
            {
                if (!args.Block)
                {
                    return Encode(new Nlm4Res { Cookie = args.Cookie, Status = Nlm4Status.Denied });
                }

                string blockedKey = FileKey(args.Lock);
                if (!_pending.TryGetValue(blockedKey, out Queue<Pending>? pending))
                {
                    pending = new Queue<Pending>();
                    _pending[blockedKey] = pending;
                }

                pending.Enqueue(new Pending(args.Cookie ?? [], args.Lock, args.Exclusive));
                return Encode(new Nlm4Res { Cookie = args.Cookie ?? [], Status = Nlm4Status.Blocked });
            }

            AddHeld(args.Lock, args.Exclusive);
            reply = Encode(new Nlm4Res { Cookie = args.Cookie ?? [], Status = Nlm4Status.Granted });
            ProcessWaiters(FileKey(args.Lock), grants);
        }

        await NotifyGrantedAsync(grants, cancellationToken).ConfigureAwait(false);
        return reply;
    }

    private async ValueTask<RpcReplyPayload> UnlockAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nlm4UnlockArgs args = Decode<Nlm4UnlockArgs>(arguments);
        List<Pending> grants = [];
        RpcReplyPayload reply;
        lock (_gate)
        {
            string key = FileKey(args.Lock);
            if (_locks.TryGetValue(key, out List<Held>? held))
            {
                string ownerKey = OwnerKey(args.Lock.Owner, args.Lock.ServerId);
                held.RemoveAll(h =>
                    OwnerKey(h.Owner, h.ServerId) == ownerKey &&
                    Overlaps(args.Lock.Offset, args.Lock.Length, h.Offset, h.Length));
                if (held.Count == 0)
                {
                    _locks.Remove(key);
                }
            }

            ProcessWaiters(key, grants);
            reply = Encode(new Nlm4Res { Cookie = args.Cookie ?? [], Status = Nlm4Status.Granted });
        }

        await NotifyGrantedAsync(grants, cancellationToken).ConfigureAwait(false);
        return reply;
    }

    private async ValueTask<RpcReplyPayload> CancelAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nlm4CancelArgs args = Decode<Nlm4CancelArgs>(arguments);
        List<Pending> grants = [];
        RpcReplyPayload reply;
        lock (_gate)
        {
            string key = FileKey(args.Lock);
            if (_pending.TryGetValue(key, out Queue<Pending>? pending))
            {
                int count = pending.Count;
                for (int i = 0; i < count; i++)
                {
                    Pending waiter = pending.Dequeue();
                    if (!Matches(waiter.Lock, args.Lock) || waiter.Exclusive != args.Exclusive)
                    {
                        pending.Enqueue(waiter);
                    }
                }

                if (pending.Count == 0)
                {
                    _pending.Remove(key);
                }
                else
                {
                    ProcessWaiters(key, grants);
                }
            }

            reply = Encode(new Nlm4Res { Cookie = args.Cookie ?? [], Status = Nlm4Status.Granted });
        }

        await NotifyGrantedAsync(grants, cancellationToken).ConfigureAwait(false);
        return reply;
    }

    private Held? FindConflict(Nlm4Lock request, bool exclusive)
    {
        string key = FileKey(request);
        if (!_locks.TryGetValue(key, out List<Held>? held))
        {
            return null;
        }

        string ownerKey = OwnerKey(request.Owner, request.ServerId);
        foreach (Held existing in held)
        {
            if (OwnerKey(existing.Owner, existing.ServerId) == ownerKey)
            {
                continue;
            }

            if ((exclusive || existing.Exclusive) &&
                Overlaps(request.Offset, request.Length, existing.Offset, existing.Length))
            {
                return existing;
            }
        }

        return null;
    }

    private static bool Overlaps(ulong offsetA, ulong lengthA, ulong offsetB, ulong lengthB)
    {
        ulong endA = End(offsetA, lengthA);
        ulong endB = End(offsetB, lengthB);
        return offsetA < endB && offsetB < endA;
    }

    private static ulong End(ulong offset, ulong length)
    {
        if (length == 0 || offset + length < offset)
        {
            return ulong.MaxValue; // A length of zero means "to the end of the file".
        }

        return offset + length;
    }

    private static string OwnerKey(byte[]? owner, int serverId) =>
        serverId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) +
        Convert.ToHexString(owner ?? []);

    private void AddHeld(Nlm4Lock @lock, bool exclusive)
    {
        string key = FileKey(@lock);
        if (!_locks.TryGetValue(key, out List<Held>? held))
        {
            held = [];
            _locks[key] = held;
        }

        held.Add(new Held(
            @lock.CallerName ?? string.Empty,
            @lock.Owner ?? [],
            @lock.ServerId,
            exclusive,
            @lock.Offset,
            @lock.Length));
    }

    private void ProcessWaiters(string key, List<Pending> grants)
    {
        if (!_pending.TryGetValue(key, out Queue<Pending>? pending))
        {
            return;
        }

        while (pending.TryPeek(out Pending waiter) && FindConflict(waiter.Lock, waiter.Exclusive) is null)
        {
            _ = pending.Dequeue();
            AddHeld(waiter.Lock, waiter.Exclusive);
            grants.Add(waiter);
        }

        if (pending.Count == 0)
        {
            _pending.Remove(key);
        }
    }

    private static string FileKey(Nlm4Lock @lock) => Convert.ToHexString(@lock.FileHandle ?? []);

    private static bool Matches(Nlm4Lock left, Nlm4Lock right) =>
        string.Equals(left.CallerName, right.CallerName, StringComparison.Ordinal) &&
        OwnerKey(left.Owner, left.ServerId) == OwnerKey(right.Owner, right.ServerId) &&
        left.Offset == right.Offset &&
        left.Length == right.Length &&
        Convert.ToHexString(left.FileHandle ?? []) == Convert.ToHexString(right.FileHandle ?? []);

    private static bool HostMatches(string lockCallerName, string notifiedHost)
    {
        string left = HostPart(lockCallerName);
        string right = HostPart(notifiedHost);
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string HostPart(string callerName)
    {
        int colon = callerName.LastIndexOf(':');
        return colon > 0 ? callerName[..colon] : callerName;
    }

    private static async Task NotifyGrantedAsync(IEnumerable<Pending> grants, CancellationToken cancellationToken)
    {
        foreach (Pending grant in grants)
        {
            if (TryGetCallbackEndPoint(grant.Lock.CallerName, out IPEndPoint? endPoint))
            {
                await using RpcClient rpc = await RpcClient.ConnectAsync(endPoint!, cancellationToken).ConfigureAwait(false);
                RpcReply reply = await rpc.CallAsync(
                    Nlm4.Program,
                    Nlm4.ProtocolVersion,
                    (uint)Nlm4Procedure.Granted,
                    OpaqueAuth.None,
                    OpaqueAuth.None,
                    new Nlm4TestArgs { Cookie = grant.Cookie, Exclusive = grant.Exclusive, Lock = grant.Lock },
                    cancellationToken).ConfigureAwait(false);

                if (!reply.IsSuccess)
                {
                    throw new RpcException("The NLM_GRANTED callback was not accepted.");
                }
            }
        }
    }

    private static bool TryGetCallbackEndPoint(string? callerName, out IPEndPoint? endPoint)
    {
        endPoint = null;
        if (string.IsNullOrWhiteSpace(callerName))
        {
            return false;
        }

        int colon = callerName.LastIndexOf(':');
        if (colon <= 0 ||
            colon == callerName.Length - 1 ||
            !int.TryParse(callerName[(colon + 1)..], out int port))
        {
            return false;
        }

        string host = callerName[..colon];
        IPAddress address;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            address = IPAddress.Loopback;
        }
        else if (!IPAddress.TryParse(host, out address!))
        {
            return false;
        }

        endPoint = new IPEndPoint(address, port);
        return true;
    }

    private static T Decode<T>(ReadOnlyMemory<byte> arguments)
        where T : IXdrSerializable<T>
    {
        var reader = new XdrReader(arguments.Span);
        return T.ReadFrom(ref reader);
    }

    private static RpcReplyPayload Encode<T>(T result)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }

    private readonly record struct Held(
        string CallerName,
        byte[] Owner,
        int ServerId,
        bool Exclusive,
        ulong Offset,
        ulong Length);

    private readonly record struct Pending(byte[] Cookie, Nlm4Lock Lock, bool Exclusive);
}
