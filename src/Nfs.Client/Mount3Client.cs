using Nfs.Mount;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>
/// A typed MOUNT version 3 client. It wraps an <see cref="RpcClient"/> connected to a server's
/// MOUNT program (100005) and is used to obtain an export's root file handle.
/// </summary>
public sealed class Mount3Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Mount3Client(IRpcClient rpc, OpaqueAuth credential = default)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _credential = credential;
    }

    /// <summary>Mounts an export and returns its root handle.</summary>
    /// <param name="path">The export path to mount.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The MNT result.</returns>
    public async ValueTask<Mount3MountResult> MountAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        RpcReply reply = await _rpc.CallAsync(
            Mount3.Program,
            Mount3.ProtocolVersion,
            (uint)Mount3Procedure.Mount,
            _credential,
            OpaqueAuth.None,
            new Mount3MountArgs { Path = path },
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The MOUNT call was not accepted (reply {reply.Header.Accept}).");
        }

        return reply.DecodeResult<Mount3MountResult>();
    }

    /// <summary>Unmounts an export.</summary>
    /// <param name="path">The export path to unmount.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask UnmountAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        RpcReply reply = await _rpc.CallAsync(
            Mount3.Program,
            Mount3.ProtocolVersion,
            (uint)Mount3Procedure.Unmount,
            _credential,
            OpaqueAuth.None,
            new Mount3MountArgs { Path = path },
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The UMNT call was not accepted (reply {reply.Header.Accept}).");
        }
    }

    /// <summary>Returns the server's current MOUNT DUMP list.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The active mounts known to the server.</returns>
    public async ValueTask<Mount3MountList> DumpAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Mount3.Program,
            Mount3.ProtocolVersion,
            (uint)Mount3Procedure.Dump,
            _credential,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The DUMP call was not accepted (reply {reply.Header.Accept}).");
        }

        return reply.DecodeResult<Mount3MountList>();
    }

    /// <summary>Returns the exports advertised by the server.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The exported paths and group lists.</returns>
    public async ValueTask<Mount3ExportList> ExportAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Mount3.Program,
            Mount3.ProtocolVersion,
            (uint)Mount3Procedure.Export,
            _credential,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        if (!reply.IsSuccess)
        {
            throw new RpcException($"The EXPORT call was not accepted (reply {reply.Header.Accept}).");
        }

        return reply.DecodeResult<Mount3ExportList>();
    }
}
