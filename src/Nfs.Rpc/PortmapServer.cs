using System.Buffers;
using System.Collections.Concurrent;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the portmapper / rpcbind program (100000, version 2).
/// It keeps an in-memory registration table and answers NULL, SET, UNSET, and GETPORT so that a
/// process can advertise the ports of its other RPC programs (NFS, MOUNT, NLM) to clients.
/// </summary>
public sealed class PortmapServer : IRpcProgram
{
    private readonly ConcurrentDictionary<(uint Program, uint Version, uint Protocol), uint> _mappings = new();

    /// <inheritdoc/>
    public uint Program => Portmap.Program;

    /// <summary>Registers a port mapping for a program/version on a transport.</summary>
    /// <param name="program">The program number.</param>
    /// <param name="version">The program version.</param>
    /// <param name="protocol">The transport protocol.</param>
    /// <param name="port">The port the program listens on.</param>
    public void Register(uint program, uint version, PortmapProtocol protocol, int port) =>
        _mappings[(program, version, (uint)protocol)] = (uint)port;

    /// <summary>Removes a port mapping for a program/version on a transport.</summary>
    /// <param name="program">The program number.</param>
    /// <param name="version">The program version.</param>
    /// <param name="protocol">The transport protocol.</param>
    public void Unregister(uint program, uint version, PortmapProtocol protocol) =>
        _mappings.TryRemove((program, version, (uint)protocol), out _);

    /// <inheritdoc/>
    public ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Portmap.Version)
        {
            return new ValueTask<RpcReplyPayload>(
                RpcReplyPayload.ProgramMismatch(Portmap.Version, Portmap.Version));
        }

        RpcReplyPayload payload = request.Procedure switch
        {
            Portmap.NullProcedure => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Portmap.SetProcedure => Set(arguments),
            Portmap.UnsetProcedure => Unset(arguments),
            Portmap.GetPortProcedure => GetPort(arguments),
            Portmap.DumpProcedure => Dump(),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };

        return new ValueTask<RpcReplyPayload>(payload);
    }

    private RpcReplyPayload Set(ReadOnlyMemory<byte> arguments)
    {
        PortmapMapping mapping = Decode<PortmapMapping>(arguments);
        _mappings[(mapping.Program, mapping.Version, mapping.Protocol)] = mapping.Port;
        return Encode(new PortmapBool { Value = true });
    }

    private RpcReplyPayload Unset(ReadOnlyMemory<byte> arguments)
    {
        PortmapMapping mapping = Decode<PortmapMapping>(arguments);
        bool removed = _mappings.TryRemove((mapping.Program, mapping.Version, mapping.Protocol), out _);
        return Encode(new PortmapBool { Value = removed });
    }

    private RpcReplyPayload GetPort(ReadOnlyMemory<byte> arguments)
    {
        PortmapMapping mapping = Decode<PortmapMapping>(arguments);
        uint port = _mappings.TryGetValue((mapping.Program, mapping.Version, mapping.Protocol), out uint found)
            ? found
            : 0;
        return Encode(new PortmapPort { Port = port });
    }

    private RpcReplyPayload Dump()
    {
        PortmapMapping[] mappings = [.. _mappings.Select(mapping => new PortmapMapping
        {
            Program = mapping.Key.Program,
            Version = mapping.Key.Version,
            Protocol = mapping.Key.Protocol,
            Port = mapping.Value,
        })];

        return Encode(new PortmapDumpResult { Mappings = mappings });
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
