using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>Well-known constants for the portmapper / rpcbind protocol (RFC 1833, version 2).</summary>
public static class Portmap
{
    /// <summary>The portmapper RPC program number.</summary>
    public const uint Program = 100000;

    /// <summary>The portmapper protocol version implemented by this client.</summary>
    public const uint Version = 2;

    /// <summary>The PMAPPROC_NULL procedure number.</summary>
    public const uint NullProcedure = 0;

    /// <summary>The PMAPPROC_SET procedure number.</summary>
    public const uint SetProcedure = 1;

    /// <summary>The PMAPPROC_UNSET procedure number.</summary>
    public const uint UnsetProcedure = 2;

    /// <summary>The PMAPPROC_GETPORT procedure number.</summary>
    public const uint GetPortProcedure = 3;

    /// <summary>The PMAPPROC_DUMP procedure number.</summary>
    public const uint DumpProcedure = 4;

    /// <summary>The well-known port the portmapper listens on.</summary>
    public const int WellKnownPort = 111;
}

/// <summary>The transport protocol of a port mapping, encoded as an IP protocol number.</summary>
public enum PortmapProtocol
{
    /// <summary>TCP (IPPROTO_TCP).</summary>
    Tcp = 6,

    /// <summary>UDP (IPPROTO_UDP).</summary>
    Udp = 17,
}

/// <summary>A single portmapper mapping entry.</summary>
[XdrType]
public partial struct PortmapMapping
{
    /// <summary>The RPC program number.</summary>
    [XdrField(0)]
    public uint Program { get; set; }

    /// <summary>The RPC program version.</summary>
    [XdrField(1)]
    public uint Version { get; set; }

    /// <summary>The transport protocol number.</summary>
    [XdrField(2)]
    public uint Protocol { get; set; }

    /// <summary>The registered transport port.</summary>
    [XdrField(3)]
    public uint Port { get; set; }
}

[XdrType]
internal partial struct PortmapPort
{
    [XdrField(0)]
    public uint Port { get; set; }
}

[XdrType]
internal partial struct PortmapBool
{
    [XdrField(0)]
    public bool Value { get; set; }
}

/// <summary>The PMAPPROC_DUMP result list, encoded as the portmapper <c>pmaplist</c> linked-list type.</summary>
public record struct PortmapDumpResult : IXdrSerializable<PortmapDumpResult>
{
    /// <summary>The registered mappings.</summary>
    public PortmapMapping[] Mappings { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        foreach (PortmapMapping mapping in Mappings ?? [])
        {
            writer.WriteBool(true);
            mapping.WriteTo(ref writer);
        }

        writer.WriteBool(false);
    }

    /// <inheritdoc/>
    public static PortmapDumpResult ReadFrom(ref XdrReader reader)
    {
        var mappings = new List<PortmapMapping>();
        while (reader.ReadBool())
        {
            mappings.Add(PortmapMapping.ReadFrom(ref reader));
        }

        return new PortmapDumpResult { Mappings = [.. mappings] };
    }
}

/// <summary>A client for the portmapper / rpcbind version 2 service (RFC 1833).</summary>
public static class PortmapClient
{
    /// <summary>Registers a TCP or UDP port for a program/version pair.</summary>
    /// <param name="client">A client connected to a portmapper.</param>
    /// <param name="program">The program number to register.</param>
    /// <param name="version">The program version to register.</param>
    /// <param name="protocol">The transport protocol to register.</param>
    /// <param name="port">The port to register.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns><see langword="true"/> if the portmapper accepted the registration.</returns>
    /// <exception cref="RpcException">The portmapper did not accept the call.</exception>
    public static async ValueTask<bool> SetAsync(
        RpcClient client,
        uint program,
        uint version,
        PortmapProtocol protocol,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var mapping = new PortmapMapping
        {
            Program = program,
            Version = version,
            Protocol = (uint)protocol,
            Port = (uint)port,
        };

        RpcReply reply = await client.CallAsync(
            Portmap.Program,
            Portmap.Version,
            Portmap.SetProcedure,
            OpaqueAuth.None,
            OpaqueAuth.None,
            mapping,
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"Portmap SET was not accepted (status {reply.Header.Accept}).");
        }

        return reply.DecodeResult<PortmapBool>().Value;
    }

    /// <summary>Removes a TCP or UDP port registration for a program/version pair.</summary>
    /// <param name="client">A client connected to a portmapper.</param>
    /// <param name="program">The program number to unregister.</param>
    /// <param name="version">The program version to unregister.</param>
    /// <param name="protocol">The transport protocol to unregister.</param>
    /// <param name="port">The registered port value to send with the request.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns><see langword="true"/> if a mapping was removed.</returns>
    /// <exception cref="RpcException">The portmapper did not accept the call.</exception>
    public static async ValueTask<bool> UnsetAsync(
        RpcClient client,
        uint program,
        uint version,
        PortmapProtocol protocol,
        int port = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var mapping = new PortmapMapping
        {
            Program = program,
            Version = version,
            Protocol = (uint)protocol,
            Port = (uint)port,
        };

        RpcReply reply = await client.CallAsync(
            Portmap.Program,
            Portmap.Version,
            Portmap.UnsetProcedure,
            OpaqueAuth.None,
            OpaqueAuth.None,
            mapping,
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"Portmap UNSET was not accepted (status {reply.Header.Accept}).");
        }

        return reply.DecodeResult<PortmapBool>().Value;
    }

    /// <summary>Looks up the TCP or UDP port a program/version pair is registered on.</summary>
    /// <param name="client">A client connected to a portmapper (typically on port 111).</param>
    /// <param name="program">The program number to look up.</param>
    /// <param name="version">The program version to look up.</param>
    /// <param name="protocol">The transport protocol to look up.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The registered port, or 0 if the program is not registered.</returns>
    /// <exception cref="RpcException">The portmapper did not accept the call.</exception>
    public static async ValueTask<int> GetPortAsync(
        RpcClient client,
        uint program,
        uint version,
        PortmapProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var mapping = new PortmapMapping
        {
            Program = program,
            Version = version,
            Protocol = (uint)protocol,
            Port = 0,
        };

        RpcReply reply = await client.CallAsync(
            Portmap.Program,
            Portmap.Version,
            Portmap.GetPortProcedure,
            OpaqueAuth.None,
            OpaqueAuth.None,
            mapping,
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"Portmap GETPORT was not accepted (status {reply.Header.Accept}).");
        }

        return (int)reply.DecodeResult<PortmapPort>().Port;
    }

    /// <summary>Returns the complete mapping list from the portmapper.</summary>
    /// <param name="client">A client connected to a portmapper.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The registered mappings.</returns>
    /// <exception cref="RpcException">The portmapper did not accept the call.</exception>
    public static async ValueTask<PortmapMapping[]> DumpAsync(
        RpcClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        RpcReply reply = await client.CallAsync(
            Portmap.Program,
            Portmap.Version,
            Portmap.DumpProcedure,
            OpaqueAuth.None,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"Portmap DUMP was not accepted (status {reply.Header.Accept}).");
        }

        return reply.DecodeResult<PortmapDumpResult>().Mappings;
    }
}
