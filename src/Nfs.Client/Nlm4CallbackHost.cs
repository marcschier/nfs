using System.Buffers;
using System.Net;

using Nfs.Nlm;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>Hosts an NLM callback endpoint and surfaces incoming NLM_GRANTED calls.</summary>
public sealed class Nlm4CallbackHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Nlm4Lock> _grants = new();
    private readonly Queue<TaskCompletionSource<Nlm4Lock>> _waiters = new();
    private readonly RpcServer _server;

    private Nlm4CallbackHost(RpcServer server) => _server = server;

    /// <summary>Gets the endpoint to place in <see cref="Nlm4Lock.CallerName"/> for loopback tests.</summary>
    public IPEndPoint EndPoint => _server.LocalEndPoint;

    /// <summary>Starts a loopback callback host.</summary>
    /// <returns>The started callback host.</returns>
    public static Nlm4CallbackHost Start()
    {
        Nlm4CallbackHost? host = null;
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new CallbackProgram(grant => host!.OnGranted(grant)));
        host = new Nlm4CallbackHost(server);
        server.Start();
        return host;
    }

    /// <summary>Waits for the next granted lock callback.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The granted lock.</returns>
    public ValueTask<Nlm4Lock> WaitForGrantedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_grants.TryDequeue(out Nlm4Lock grant))
            {
                return ValueTask.FromResult(grant);
            }

            var waiter = new TaskCompletionSource<Nlm4Lock>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            }

            _waiters.Enqueue(waiter);
            return new ValueTask<Nlm4Lock>(waiter.Task);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private void OnGranted(Nlm4Lock grant)
    {
        lock (_gate)
        {
            if (_waiters.TryDequeue(out TaskCompletionSource<Nlm4Lock>? waiter))
            {
                waiter.TrySetResult(grant);
            }
            else
            {
                _grants.Enqueue(grant);
            }
        }
    }

    private sealed class CallbackProgram : IRpcProgram
    {
        private readonly Action<Nlm4Lock> _onGranted;

        public CallbackProgram(Action<Nlm4Lock> onGranted) => _onGranted = onGranted;

        public uint Program => Nlm4.Program;

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != Nlm4.ProtocolVersion)
            {
                return new ValueTask<RpcReplyPayload>(
                    RpcReplyPayload.ProgramMismatch(Nlm4.ProtocolVersion, Nlm4.ProtocolVersion));
            }

            if ((Nlm4Procedure)request.Procedure != Nlm4Procedure.Granted)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProcedureUnavailable());
            }

            Nlm4TestArgs args = Decode<Nlm4TestArgs>(arguments);
            _onGranted(args.Lock);
            return new ValueTask<RpcReplyPayload>(
                Encode(new Nlm4Res { Cookie = args.Cookie, Status = Nlm4Status.Granted }));
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
    }
}
