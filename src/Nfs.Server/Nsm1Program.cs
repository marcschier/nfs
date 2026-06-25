using System.Buffers;

using Nfs.Nsm;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>Serves the Network Status Monitor program (NSM, 100024, version 1).</summary>
public sealed class Nsm1Program : IRpcProgram
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Nsm1Monitor>> _monitors = new(StringComparer.Ordinal);
    private readonly Action<string>? _hostStateChanged;
    private int _state = 1;

    /// <summary>Creates an NSM program.</summary>
    /// <param name="hostStateChanged">Optional callback invoked when <c>SM_NOTIFY</c> reports a host reboot.</param>
    public Nsm1Program(Action<string>? hostStateChanged = null) => _hostStateChanged = hostStateChanged;

    /// <inheritdoc/>
    public uint Program => Nsm1.Program;

    /// <inheritdoc/>
    public ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Nsm1.ProtocolVersion)
        {
            return new ValueTask<RpcReplyPayload>(
                RpcReplyPayload.ProgramMismatch(Nsm1.ProtocolVersion, Nsm1.ProtocolVersion));
        }

        RpcReplyPayload payload = (Nsm1Procedure)request.Procedure switch
        {
            Nsm1Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nsm1Procedure.Stat => Stat(arguments),
            Nsm1Procedure.Monitor => Monitor(arguments),
            Nsm1Procedure.Unmonitor => Unmonitor(arguments),
            Nsm1Procedure.UnmonitorAll => UnmonitorAll(arguments),
            Nsm1Procedure.SimulateCrash => SimulateCrash(),
            Nsm1Procedure.Notify => Notify(arguments),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };

        return new ValueTask<RpcReplyPayload>(payload);
    }

    private RpcReplyPayload Stat(ReadOnlyMemory<byte> arguments)
    {
        _ = Decode<Nsm1Name>(arguments);
        lock (_gate)
        {
            return Encode(new Nsm1StatusResult { Result = Nsm1Result.Success, State = _state });
        }
    }

    private RpcReplyPayload Monitor(ReadOnlyMemory<byte> arguments)
    {
        Nsm1Monitor monitor = Decode<Nsm1Monitor>(arguments);
        string name = monitor.MonitorId.MonitorName ?? string.Empty;
        lock (_gate)
        {
            if (!_monitors.TryGetValue(name, out List<Nsm1Monitor>? monitors))
            {
                monitors = [];
                _monitors[name] = monitors;
            }

            monitors.Add(monitor);
            BumpState();
            return Encode(new Nsm1StatusResult { Result = Nsm1Result.Success, State = _state });
        }
    }

    private RpcReplyPayload Unmonitor(ReadOnlyMemory<byte> arguments)
    {
        Nsm1Name name = Decode<Nsm1Name>(arguments);
        lock (_gate)
        {
            _monitors.Remove(name.MonitorName ?? string.Empty);
            BumpState();
            return Encode(new Nsm1Status { State = _state });
        }
    }

    private RpcReplyPayload UnmonitorAll(ReadOnlyMemory<byte> arguments)
    {
        Nsm1MyId myId = Decode<Nsm1MyId>(arguments);
        lock (_gate)
        {
            foreach (string name in _monitors.Keys.ToArray())
            {
                List<Nsm1Monitor> monitors = _monitors[name];
                monitors.RemoveAll(m => SameCallback(m.MonitorId.MyId, myId));
                if (monitors.Count == 0)
                {
                    _monitors.Remove(name);
                }
            }

            BumpState();
            return Encode(new Nsm1Status { State = _state });
        }
    }

    private RpcReplyPayload SimulateCrash()
    {
        lock (_gate)
        {
            _monitors.Clear();
            BumpState();
        }

        return RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty);
    }

    private RpcReplyPayload Notify(ReadOnlyMemory<byte> arguments)
    {
        Nsm1StatusChange change = Decode<Nsm1StatusChange>(arguments);
        _hostStateChanged?.Invoke(change.MonitorName ?? string.Empty);
        return RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty);
    }

    private void BumpState() => _state += 2;

    private static bool SameCallback(Nsm1MyId left, Nsm1MyId right) =>
        string.Equals(left.MyName, right.MyName, StringComparison.Ordinal) &&
        left.Program == right.Program &&
        left.Version == right.Version &&
        left.Procedure == right.Procedure;

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
