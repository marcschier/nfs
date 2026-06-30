using Nfs.Nsm;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>A typed Network Status Monitor (NSM version 1) client.</summary>
public sealed class Nsm1Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Nsm1Client(IRpcClient rpc, OpaqueAuth credential = default)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _credential = credential;
    }

    /// <summary>Calls the NULL procedure.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask NullAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Nsm1.Program,
            Nsm1.ProtocolVersion,
            (uint)Nsm1Procedure.Null,
            _credential,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        EnsureAccepted(reply);
    }

    /// <summary>Registers a monitor callback for a host.</summary>
    /// <param name="monitor">The monitor registration.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The monitor result and server state.</returns>
    public ValueTask<Nsm1StatusResult> MonitorAsync(
        Nsm1Monitor monitor,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nsm1Monitor, Nsm1StatusResult>(Nsm1Procedure.Monitor, monitor, cancellationToken);

    /// <summary>Unregisters a monitor callback for a host.</summary>
    /// <param name="name">The monitored host name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The previous server state.</returns>
    public ValueTask<Nsm1Status> UnmonitorAsync(string name, CancellationToken cancellationToken = default) =>
        CallAsync<Nsm1Name, Nsm1Status>(
            Nsm1Procedure.Unmonitor,
            new Nsm1Name { MonitorName = name },
            cancellationToken);

    /// <summary>Unregisters every monitor callback that targets the given caller.</summary>
    /// <param name="callback">The caller's RPC callback identity whose monitors are removed.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The resulting server state.</returns>
    public ValueTask<Nsm1Status> UnmonitorAllAsync(
        Nsm1MyId callback,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nsm1MyId, Nsm1Status>(Nsm1Procedure.UnmonitorAll, callback, cancellationToken);

    /// <summary>Asks the monitor to simulate a crash, clearing its state and bumping the state number.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask SimulateCrashAsync(CancellationToken cancellationToken = default) =>
        await CallAsync<XdrVoid, XdrVoid>(Nsm1Procedure.SimulateCrash, default, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Queries the status of a monitored host.</summary>
    /// <param name="name">The host name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The status result and server state.</returns>
    public ValueTask<Nsm1StatusResult> StatAsync(string name, CancellationToken cancellationToken = default) =>
        CallAsync<Nsm1Name, Nsm1StatusResult>(
            Nsm1Procedure.Stat,
            new Nsm1Name { MonitorName = name },
            cancellationToken);

    /// <summary>Delivers a remote host state-change notification.</summary>
    /// <param name="change">The state change.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask NotifyAsync(Nsm1StatusChange change, CancellationToken cancellationToken = default) =>
        await CallAsync<Nsm1StatusChange, XdrVoid>(Nsm1Procedure.Notify, change, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<TResult> CallAsync<TArgs, TResult>(
        Nsm1Procedure procedure,
        TArgs arguments,
        CancellationToken cancellationToken)
        where TArgs : IXdrSerializable<TArgs>
        where TResult : IXdrSerializable<TResult>
    {
        RpcReply reply = await _rpc.CallAsync(
            Nsm1.Program,
            Nsm1.ProtocolVersion,
            (uint)procedure,
            _credential,
            OpaqueAuth.None,
            arguments,
            cancellationToken).ConfigureAwait(false);

        EnsureAccepted(reply);
        return reply.DecodeResult<TResult>();
    }

    private static void EnsureAccepted(RpcReply reply)
    {
        if (!reply.IsSuccess)
        {
            throw new RpcException(
                $"The NSM call was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }
    }
}
