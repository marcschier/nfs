using System.Buffers;
using System.Net;

using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>Hosts an NFSv4 callback endpoint and processes CB_SEQUENCE/CB_RECALL calls.</summary>
public sealed class Nfs4CallbackHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Nfs4CallbackRecallOp> _recalls = new();
    private readonly Queue<TaskCompletionSource<Nfs4CallbackRecallOp>> _waiters = new();
    private readonly Queue<Nfs4CallbackOffloadOp> _offloads = new();
    private readonly Queue<TaskCompletionSource<Nfs4CallbackOffloadOp>> _offloadWaiters = new();
    private readonly Queue<Nfs4CallbackNotifyLockOp> _lockNotifications = new();
    private readonly Queue<TaskCompletionSource<Nfs4CallbackNotifyLockOp>> _lockNotificationWaiters = new();
    private readonly RpcServer _server;

    private Nfs4CallbackHost(RpcServer server) => _server = server;

    /// <summary>Gets the callback endpoint.</summary>
    public IPEndPoint EndPoint => _server.LocalEndPoint;

    /// <summary>Starts a loopback callback host.</summary>
    /// <param name="program">The callback RPC program number to serve.</param>
    /// <returns>The started callback host.</returns>
    public static Nfs4CallbackHost Start(uint program)
    {
        Nfs4CallbackHost? host = null;
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new CallbackProgram(
                program,
                recall => host!.OnRecall(recall),
                offload => host!.OnOffload(offload),
                notifyLock => host!.OnNotifyLock(notifyLock)));
        host = new Nfs4CallbackHost(server);
        server.Start();
        return host;
    }

    /// <summary>Waits for the next CB_RECALL.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The recalled delegation.</returns>
    public ValueTask<Nfs4CallbackRecallOp> WaitForRecallAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_recalls.TryDequeue(out Nfs4CallbackRecallOp? recall))
            {
                return new ValueTask<Nfs4CallbackRecallOp>(recall);
            }

            var waiter = new TaskCompletionSource<Nfs4CallbackRecallOp>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            }

            _waiters.Enqueue(waiter);
            return new ValueTask<Nfs4CallbackRecallOp>(waiter.Task);
        }
    }

    /// <summary>Waits for the next CB_OFFLOAD.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The offload completion callback.</returns>
    public ValueTask<Nfs4CallbackOffloadOp> WaitForOffloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_offloads.TryDequeue(out Nfs4CallbackOffloadOp? offload))
            {
                return new ValueTask<Nfs4CallbackOffloadOp>(offload);
            }

            var waiter = new TaskCompletionSource<Nfs4CallbackOffloadOp>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            }

            _offloadWaiters.Enqueue(waiter);
            return new ValueTask<Nfs4CallbackOffloadOp>(waiter.Task);
        }
    }

    /// <summary>Waits for the next CB_NOTIFY_LOCK.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The lock notification callback.</returns>
    public ValueTask<Nfs4CallbackNotifyLockOp> WaitForLockNotificationAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_lockNotifications.TryDequeue(out Nfs4CallbackNotifyLockOp? notifyLock))
            {
                return new ValueTask<Nfs4CallbackNotifyLockOp>(notifyLock);
            }

            var waiter = new TaskCompletionSource<Nfs4CallbackNotifyLockOp>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            }

            _lockNotificationWaiters.Enqueue(waiter);
            return new ValueTask<Nfs4CallbackNotifyLockOp>(waiter.Task);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _server.DisposeAsync();

    private void OnRecall(Nfs4CallbackRecallOp recall)
    {
        lock (_gate)
        {
            if (_waiters.TryDequeue(out TaskCompletionSource<Nfs4CallbackRecallOp>? waiter))
            {
                waiter.TrySetResult(recall);
            }
            else
            {
                _recalls.Enqueue(recall);
            }
        }
    }

    private void OnOffload(Nfs4CallbackOffloadOp offload)
    {
        lock (_gate)
        {
            if (_offloadWaiters.TryDequeue(out TaskCompletionSource<Nfs4CallbackOffloadOp>? waiter))
            {
                waiter.TrySetResult(offload);
            }
            else
            {
                _offloads.Enqueue(offload);
            }
        }
    }

    private void OnNotifyLock(Nfs4CallbackNotifyLockOp notifyLock)
    {
        lock (_gate)
        {
            if (_lockNotificationWaiters.TryDequeue(out TaskCompletionSource<Nfs4CallbackNotifyLockOp>? waiter))
            {
                waiter.TrySetResult(notifyLock);
            }
            else
            {
                _lockNotifications.Enqueue(notifyLock);
            }
        }
    }

    private sealed class CallbackProgram : IRpcProgram
    {
        private readonly Action<Nfs4CallbackRecallOp> _onRecall;
        private readonly Action<Nfs4CallbackOffloadOp> _onOffload;
        private readonly Action<Nfs4CallbackNotifyLockOp> _onNotifyLock;

        public CallbackProgram(
            uint program,
            Action<Nfs4CallbackRecallOp> onRecall,
            Action<Nfs4CallbackOffloadOp> onOffload,
            Action<Nfs4CallbackNotifyLockOp> onNotifyLock)
        {
            Program = program;
            _onRecall = onRecall;
            _onOffload = onOffload;
            _onNotifyLock = onNotifyLock;
        }

        public uint Program { get; }

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != Nfs4.ProtocolVersion)
            {
                return new ValueTask<RpcReplyPayload>(
                    RpcReplyPayload.ProgramMismatch(Nfs4.ProtocolVersion, Nfs4.ProtocolVersion));
            }

            if ((Nfs4CallbackProcedure)request.Procedure != Nfs4CallbackProcedure.Compound)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProcedureUnavailable());
            }

            Nfs4CallbackCompoundArgs args = Decode(arguments);
            var result = new Nfs4CallbackCompoundResult { Status = Nfs4Status.Ok, Tag = args.Tag };
            foreach (Nfs4CallbackArgOp operation in args.Operations)
            {
                Nfs4CallbackResOp resop = Execute(operation);
                result.Operations.Add(resop);
                result.OperationStatuses.Add(resop.Status);
                if (resop.Status != Nfs4Status.Ok)
                {
                    result.Status = resop.Status;
                    break;
                }
            }

            return new ValueTask<RpcReplyPayload>(Encode(result));
        }

        private Nfs4CallbackResOp Execute(Nfs4CallbackArgOp operation)
        {
            return operation switch
            {
                Nfs4CallbackSequenceOp sequence => new Nfs4CallbackSequenceResult
                {
                    Status = Nfs4Status.Ok,
                    SessionId = sequence.SessionId,
                    SequenceId = sequence.SequenceId,
                    SlotId = sequence.SlotId,
                    HighestSlotId = sequence.HighestSlotId,
                    TargetHighestSlotId = sequence.HighestSlotId,
                },
                Nfs4CallbackRecallOp recall => Recall(recall),
                Nfs4CallbackOffloadOp offload => Offload(offload),
                Nfs4CallbackNotifyLockOp notifyLock => NotifyLock(notifyLock),
                _ => new Nfs4CallbackStatusResult(operation.Op) { Status = Nfs4Status.NotSupported },
            };
        }

        private Nfs4CallbackStatusResult Recall(Nfs4CallbackRecallOp recall)
        {
            _onRecall(recall);
            return new Nfs4CallbackStatusResult(Nfs4CallbackOp.Recall) { Status = Nfs4Status.Ok };
        }

        private Nfs4CallbackStatusResult Offload(Nfs4CallbackOffloadOp offload)
        {
            _onOffload(offload);
            return new Nfs4CallbackStatusResult(Nfs4CallbackOp.Offload) { Status = Nfs4Status.Ok };
        }

        private Nfs4CallbackStatusResult NotifyLock(Nfs4CallbackNotifyLockOp notifyLock)
        {
            _onNotifyLock(notifyLock);
            return new Nfs4CallbackStatusResult(Nfs4CallbackOp.NotifyLock) { Status = Nfs4Status.Ok };
        }

        private static Nfs4CallbackCompoundArgs Decode(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            return Nfs4CallbackCompoundArgs.ReadFrom(ref reader);
        }

        private static RpcReplyPayload Encode(Nfs4CallbackCompoundResult result)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            result.WriteTo(ref writer);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
