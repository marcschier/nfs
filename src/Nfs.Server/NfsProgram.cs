using System.Net;

using Nfs.Abstractions;
using Nfs.Protocol.V2;
using Nfs.Protocol.V3;
using Nfs.Protocol.V4;
using Nfs.Rpc;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the NFS program (100003) for multiple protocol
/// versions over a single <see cref="INfsFileSystem"/>, dispatching each call to the matching
/// version handler. Supports NFS versions 2, 3, and 4.0.
/// </summary>
public sealed class NfsProgram : IRpcProgram, IRpcSecurityAware, IRpcLocalEndPointAware
{
    private readonly Nfs2Program _version2;
    private readonly Nfs3Program _version3;
    private readonly Nfs4Program _version4;

    /// <summary>Creates a multi-version handler backed by <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The storage backend to serve.</param>
    /// <param name="timeProvider">The clock used by the NFSv4 lease and grace state manager.</param>
    /// <param name="rpcSecGssEnabled">Whether NFSv4 SECINFO should advertise RPCSEC_GSS flavors.</param>
    /// <param name="stableStorage">The stable storage used for NFSv4 client recovery records.</param>
    /// <param name="pnfsOptions">The optional pNFS files-layout device configuration.</param>
    public NfsProgram(
        INfsFileSystem fileSystem,
        TimeProvider? timeProvider = null,
        bool rpcSecGssEnabled = false,
        IStableStorage? stableStorage = null,
        Nfs4PnfsOptions? pnfsOptions = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _version2 = new Nfs2Program(fileSystem);
        _version3 = new Nfs3Program(fileSystem);
        _version4 = new Nfs4Program(
            fileSystem,
            timeProvider,
            rpcSecGssEnabled: rpcSecGssEnabled,
            stableStorage: stableStorage,
            pnfsOptions: pnfsOptions);
    }

    /// <inheritdoc/>
    public uint Program => Nfs3.Program;

    /// <inheritdoc/>
    public ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken) =>
        request.Version switch
        {
            Nfs2.ProtocolVersion => _version2.InvokeAsync(request, arguments, cancellationToken),
            Nfs3.ProtocolVersion => _version3.InvokeAsync(request, arguments, cancellationToken),
            Nfs4.ProtocolVersion => _version4.InvokeAsync(request, arguments, cancellationToken),
            _ => new ValueTask<RpcReplyPayload>(
                RpcReplyPayload.ProgramMismatch(Nfs2.ProtocolVersion, Nfs4.ProtocolVersion)),
        };

    /// <inheritdoc/>
    public void SetRpcSecGssEnabled(bool enabled) => _version4.SetRpcSecGssEnabled(enabled);

    /// <inheritdoc/>
    public void SetRpcLocalEndPoint(IPEndPoint endPoint) => _version4.SetRpcLocalEndPoint(endPoint);
}
