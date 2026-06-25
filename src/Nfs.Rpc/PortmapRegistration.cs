using System.Net;

namespace Nfs.Rpc;

/// <summary>Best-effort lifetime helper for registering an RPC program with a portmapper.</summary>
public sealed class PortmapRegistration : IAsyncDisposable
{
    private readonly EndPoint _portmapperEndPoint;
    private readonly uint _program;
    private readonly uint _version;
    private readonly PortmapProtocol _protocol;
    private readonly int _port;
    private readonly bool _registered;

    private PortmapRegistration(
        EndPoint portmapperEndPoint,
        uint program,
        uint version,
        PortmapProtocol protocol,
        int port,
        bool registered)
    {
        _portmapperEndPoint = portmapperEndPoint;
        _program = program;
        _version = version;
        _protocol = protocol;
        _port = port;
        _registered = registered;
    }

    /// <summary>
    /// Attempts to register a mapping with the supplied portmapper endpoint.
    /// Connection or RPC failures are swallowed so a missing system rpcbind does not prevent startup.
    /// </summary>
    /// <param name="portmapperEndPoint">The portmapper endpoint, typically 127.0.0.1:111.</param>
    /// <param name="program">The RPC program number.</param>
    /// <param name="version">The RPC program version.</param>
    /// <param name="protocol">The transport protocol.</param>
    /// <param name="port">The local RPC server port.</param>
    /// <param name="cancellationToken">A token to cancel the registration attempt.</param>
    /// <returns>A disposable registration handle; disposing best-effort UNSETs the mapping.</returns>
    public static async ValueTask<PortmapRegistration> RegisterAsync(
        EndPoint portmapperEndPoint,
        uint program,
        uint version,
        PortmapProtocol protocol,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portmapperEndPoint);

        bool registered = false;
        try
        {
            await using RpcClient client = await RpcClient.ConnectAsync(portmapperEndPoint, cancellationToken)
                .ConfigureAwait(false);
            registered = await PortmapClient.SetAsync(client, program, version, protocol, port, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031, RCS1075 // Best-effort registration must not fail server startup.
        catch (Exception)
        {
            registered = false;
        }
#pragma warning restore CA1031, RCS1075

        return new PortmapRegistration(portmapperEndPoint, program, version, protocol, port, registered);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            await using RpcClient client = await RpcClient.ConnectAsync(_portmapperEndPoint).ConfigureAwait(false);
            _ = await PortmapClient.UnsetAsync(client, _program, _version, _protocol, _port).ConfigureAwait(false);
        }
#pragma warning disable CA1031, RCS1075 // Best-effort unregister during shutdown.
        catch (Exception)
        {
        }
#pragma warning restore CA1031, RCS1075
    }
}
