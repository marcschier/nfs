using System.Net;

using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>Small NFSv4 callback-channel client used by the server to recall delegations.</summary>
public static class Nfs4CallbackClient
{
    /// <summary>Calls CB_NULL on a client callback program.</summary>
    /// <param name="endPoint">The callback endpoint.</param>
    /// <param name="callbackProgram">The client-provided callback program number.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask NullAsync(
        EndPoint endPoint,
        uint callbackProgram,
        CancellationToken cancellationToken = default)
    {
        await using RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        RpcReply reply = await rpc.CallAsync(
            callbackProgram,
            Nfs4.ProtocolVersion,
            (uint)Nfs4CallbackProcedure.Null,
            OpaqueAuth.None,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new RpcException($"CB_NULL failed with status {reply.Header.Status}/{reply.Header.Accept}.");
        }
    }

    /// <summary>Sends CB_RECALL for a delegation.</summary>
    /// <param name="endPoint">The callback endpoint.</param>
    /// <param name="callbackProgram">The client-provided callback program number.</param>
    /// <param name="callbackIdent">The callback identifier from SETCLIENTID.</param>
    /// <param name="stateId">The delegation state identifier.</param>
    /// <param name="handle">The delegated file handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The callback status.</returns>
    public static async ValueTask<Nfs4Status> RecallAsync(
        EndPoint endPoint,
        uint callbackProgram,
        uint callbackIdent,
        Nfs4StateId stateId,
        Nfs4Handle handle,
        CancellationToken cancellationToken = default)
    {
        var args = new Nfs4CallbackCompoundArgs
        {
            Tag = "recall",
            MinorVersion = Nfs4.MinorVersion0,
            CallbackIdent = callbackIdent,
        };
        args.Operations.Add(new Nfs4CallbackRecallOp { StateId = stateId, Handle = handle });

        await using RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        RpcReply reply = await rpc.CallAsync(
            callbackProgram,
            Nfs4.ProtocolVersion,
            (uint)Nfs4CallbackProcedure.Compound,
            OpaqueAuth.None,
            OpaqueAuth.None,
            args,
            cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new RpcException($"CB_RECALL failed with status {reply.Header.Status}/{reply.Header.Accept}.");
        }

        Nfs4CallbackCompoundResult result = reply.DecodeResult<Nfs4CallbackCompoundResult>();
        return result.Status;
    }


    /// <summary>Sends a session back-channel CB_SEQUENCE + CB_RECALL for a delegation.</summary>
    /// <param name="transport">The back-channel transport.</param>
    /// <param name="backChannelCall">The session back-channel call parameters.</param>
    /// <param name="stateId">The delegation state identifier.</param>
    /// <param name="handle">The delegated file handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The callback status.</returns>
    public static async ValueTask<Nfs4Status> RecallSessionAsync(
        INfs41BackChannelTransport transport,
        Nfs41BackChannelCall backChannelCall,
        Nfs4StateId stateId,
        Nfs4Handle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var args = new Nfs4CallbackCompoundArgs
        {
            Tag = "recall",
            MinorVersion = Nfs4.MinorVersion1,
            CallbackIdent = 0,
        };
        args.Operations.Add(new Nfs4CallbackSequenceOp
        {
            SessionId = backChannelCall.SessionId,
            SequenceId = backChannelCall.SequenceId,
            SlotId = backChannelCall.SlotId,
            HighestSlotId = backChannelCall.HighestSlotId,
            CacheThis = false,
        });
        args.Operations.Add(new Nfs4CallbackRecallOp { StateId = stateId, Handle = handle });

        Nfs4CallbackCompoundResult result = await transport.CallAsync(
            new Nfs41BackChannelCall(
                backChannelCall.ClientId,
                backChannelCall.SessionId,
                backChannelCall.CallbackProgram,
                backChannelCall.SequenceId,
                backChannelCall.SlotId,
                backChannelCall.HighestSlotId,
                args),
            cancellationToken).ConfigureAwait(false);
        return result.Status;
    }

    /// <summary>Sends a session back-channel CB_SEQUENCE + CB_OFFLOAD for an asynchronous copy.</summary>
    /// <param name="transport">The back-channel transport.</param>
    /// <param name="backChannelCall">The session back-channel call parameters.</param>
    /// <param name="stateId">The copy offload state identifier.</param>
    /// <param name="status">The final copy status.</param>
    /// <param name="response">The final write response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The callback status.</returns>
    public static async ValueTask<Nfs4Status> OffloadSessionAsync(
        INfs41BackChannelTransport transport,
        Nfs41BackChannelCall backChannelCall,
        Nfs4StateId stateId,
        Nfs4Status status,
        Nfs4CopyWriteResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var args = new Nfs4CallbackCompoundArgs
        {
            Tag = "offload",
            MinorVersion = Nfs4.MinorVersion1,
            CallbackIdent = 0,
        };
        args.Operations.Add(new Nfs4CallbackSequenceOp
        {
            SessionId = backChannelCall.SessionId,
            SequenceId = backChannelCall.SequenceId,
            SlotId = backChannelCall.SlotId,
            HighestSlotId = backChannelCall.HighestSlotId,
            CacheThis = false,
        });
        args.Operations.Add(new Nfs4CallbackOffloadOp { StateId = stateId, Status = status, Response = response });

        Nfs4CallbackCompoundResult result = await transport.CallAsync(
            new Nfs41BackChannelCall(
                backChannelCall.ClientId,
                backChannelCall.SessionId,
                backChannelCall.CallbackProgram,
                backChannelCall.SequenceId,
                backChannelCall.SlotId,
                backChannelCall.HighestSlotId,
                args),
            cancellationToken).ConfigureAwait(false);
        return result.Status;
    }

    /// <summary>Sends a session back-channel CB_SEQUENCE + CB_NOTIFY_LOCK for a lock waiter.</summary>
    /// <param name="transport">The back-channel transport.</param>
    /// <param name="backChannelCall">The session back-channel call parameters.</param>
    /// <param name="handle">The file handle containing the denied lock range.</param>
    /// <param name="owner">The waiting lock owner.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The callback status.</returns>
    public static async ValueTask<Nfs4Status> NotifyLockSessionAsync(
        INfs41BackChannelTransport transport,
        Nfs41BackChannelCall backChannelCall,
        Nfs4Handle handle,
        Nfs4LockOwner owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var args = new Nfs4CallbackCompoundArgs
        {
            Tag = "notify-lock",
            MinorVersion = Nfs4.MinorVersion1,
            CallbackIdent = 0,
        };
        args.Operations.Add(new Nfs4CallbackSequenceOp
        {
            SessionId = backChannelCall.SessionId,
            SequenceId = backChannelCall.SequenceId,
            SlotId = backChannelCall.SlotId,
            HighestSlotId = backChannelCall.HighestSlotId,
            CacheThis = false,
        });
        args.Operations.Add(new Nfs4CallbackNotifyLockOp { Handle = handle, Owner = owner });

        Nfs4CallbackCompoundResult result = await transport.CallAsync(
            new Nfs41BackChannelCall(
                backChannelCall.ClientId,
                backChannelCall.SessionId,
                backChannelCall.CallbackProgram,
                backChannelCall.SequenceId,
                backChannelCall.SlotId,
                backChannelCall.HighestSlotId,
                args),
            cancellationToken).ConfigureAwait(false);
        return result.Status;
    }

    /// <summary>Parses an IPv4 TCP universal address such as <c>127.0.0.1.8.1</c>.</summary>
    /// <param name="universalAddress">The universal address.</param>
    /// <returns>The endpoint represented by the address.</returns>
    public static IPEndPoint ParseTcpUniversalAddress(string universalAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(universalAddress);
        string[] parts = universalAddress.Split('.');
        if (parts.Length != 6)
        {
            throw new FormatException("Expected an IPv4 universal address with six dot-separated numbers.");
        }

        byte[] address = new byte[4];
        for (int i = 0; i < address.Length; i++)
        {
            address[i] = byte.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        }

        int high = byte.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
        int low = byte.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
        return new IPEndPoint(new IPAddress(address), (high << 8) | low);
    }
}

/// <summary>Transports NFSv4.1 callback COMPOUND calls over a session back channel.</summary>
public interface INfs41BackChannelTransport
{
    /// <summary>Sends a callback COMPOUND.</summary>
    /// <param name="backChannelCall">The callback call.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The decoded callback compound result.</returns>
    ValueTask<Nfs4CallbackCompoundResult> CallAsync(
        Nfs41BackChannelCall backChannelCall,
        CancellationToken cancellationToken = default);
}

/// <summary>Transports NFSv4.1 callback COMPOUND calls over the accepted fore-channel TCP connection.</summary>
public sealed class Nfs41ConnectionBackChannelTransport : INfs41BackChannelTransport
{
    private readonly RpcDuplexConnection _connection;

    /// <summary>Creates a connection-bound NFSv4.1 back-channel transport.</summary>
    /// <param name="connection">The bidirectional RPC connection that owns the session.</param>
    public Nfs41ConnectionBackChannelTransport(RpcDuplexConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc/>
    public async ValueTask<Nfs4CallbackCompoundResult> CallAsync(
        Nfs41BackChannelCall backChannelCall,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backChannelCall);
        RpcReply reply = await _connection.CallAsync(
            backChannelCall.CallbackProgram,
            Nfs4.ProtocolVersion,
            (uint)Nfs4CallbackProcedure.Compound,
            OpaqueAuth.None,
            OpaqueAuth.None,
            backChannelCall.Compound,
            cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new RpcException($"CB_COMPOUND failed with status {reply.Header.Status}/{reply.Header.Accept}.");
        }

        return reply.DecodeResult<Nfs4CallbackCompoundResult>();
    }
}

/// <summary>Parameters for a single NFSv4.1 session back-channel callback call.</summary>
public sealed class Nfs41BackChannelCall
{
    /// <summary>Creates a back-channel call descriptor.</summary>
    /// <param name="clientId">The owning NFSv4.1 client identifier.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="callbackProgram">The callback program number from CREATE_SESSION.</param>
    /// <param name="sequenceId">The callback slot sequence id.</param>
    /// <param name="slotId">The callback slot id.</param>
    /// <param name="highestSlotId">The highest callback slot id.</param>
    /// <param name="compound">The callback compound arguments.</param>
    public Nfs41BackChannelCall(
        ulong clientId,
        byte[] sessionId,
        uint callbackProgram,
        uint sequenceId,
        uint slotId,
        uint highestSlotId,
        Nfs4CallbackCompoundArgs compound)
    {
        ClientId = clientId;
        SessionId = sessionId;
        CallbackProgram = callbackProgram;
        SequenceId = sequenceId;
        SlotId = slotId;
        HighestSlotId = highestSlotId;
        Compound = compound;
    }

    /// <summary>Gets the owning NFSv4.1 client identifier.</summary>
    public ulong ClientId { get; }

    /// <summary>Gets the session identifier.</summary>
    public byte[] SessionId { get; }

    /// <summary>Gets the callback program number from CREATE_SESSION.</summary>
    public uint CallbackProgram { get; }

    /// <summary>Gets the callback slot sequence id.</summary>
    public uint SequenceId { get; }

    /// <summary>Gets the callback slot id.</summary>
    public uint SlotId { get; }

    /// <summary>Gets the highest callback slot id.</summary>
    public uint HighestSlotId { get; }

    /// <summary>Gets the callback compound arguments.</summary>
    public Nfs4CallbackCompoundArgs Compound { get; }
}
