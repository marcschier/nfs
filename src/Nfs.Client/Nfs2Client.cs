using Nfs.Protocol.V2;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>
/// A typed NFS version 2 client. It wraps an <see cref="RpcClient"/>, encoding procedure arguments
/// and decoding results for the NFS program (100003, version 2).
/// </summary>
public sealed class Nfs2Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Nfs2Client(IRpcClient rpc, OpaqueAuth credential = default)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _credential = credential;
    }

    /// <summary>Calls the NULL procedure, which does nothing but exercise the connection.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask NullAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Nfs2.Program,
            Nfs2.ProtocolVersion,
            (uint)Nfs2Procedure.Null,
            _credential,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        EnsureAccepted(reply);
    }

    /// <summary>Gets the attributes of the object identified by <paramref name="handle"/>.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The GETATTR result.</returns>
    public ValueTask<Nfs2AttrStat> GetAttributesAsync(
        Nfs2Handle handle,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2HandleArgs, Nfs2AttrStat>(
            Nfs2Procedure.GetAttributes, new Nfs2HandleArgs { Handle = handle }, cancellationToken);

    /// <summary>Applies a set of attribute changes to an object.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="attributes">The attributes to set.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The SETATTR result.</returns>
    public ValueTask<Nfs2AttrStat> SetAttributesAsync(
        Nfs2Handle handle,
        Nfs2SetAttributes attributes,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2SetAttrArgs, Nfs2AttrStat>(
            Nfs2Procedure.SetAttributes,
            new Nfs2SetAttrArgs { Handle = handle, Attributes = attributes },
            cancellationToken);

    /// <summary>Resolves <paramref name="name"/> within the directory <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="name">The name to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LOOKUP result.</returns>
    public ValueTask<Nfs2DirOpResult> LookupAsync(
        Nfs2Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2DirOpArgs, Nfs2DirOpResult>(
            Nfs2Procedure.Lookup,
            new Nfs2DirOpArgs { Directory = directory, Name = name },
            cancellationToken);

    /// <summary>Reads the target path of a symbolic link.</summary>
    /// <param name="handle">The link's handle.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READLINK result.</returns>
    public ValueTask<Nfs2ReadLinkResult> ReadSymbolicLinkAsync(
        Nfs2Handle handle,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2HandleArgs, Nfs2ReadLinkResult>(
            Nfs2Procedure.ReadLink, new Nfs2HandleArgs { Handle = handle }, cancellationToken);

    /// <summary>Reads up to <paramref name="count"/> bytes from a file.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READ result.</returns>
    public ValueTask<Nfs2ReadResult> ReadAsync(
        Nfs2Handle file,
        uint offset,
        uint count,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2ReadArgs, Nfs2ReadResult>(
            Nfs2Procedure.Read,
            new Nfs2ReadArgs { File = file, Offset = offset, Count = count, TotalCount = count },
            cancellationToken);

    /// <summary>Writes <paramref name="data"/> to a file.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to write at.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The WRITE result.</returns>
    public ValueTask<Nfs2AttrStat> WriteAsync(
        Nfs2Handle file,
        uint offset,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CallAsync<Nfs2WriteArgs, Nfs2AttrStat>(
            Nfs2Procedure.Write,
            new Nfs2WriteArgs
            {
                File = file,
                BeginOffset = offset,
                Offset = offset,
                TotalCount = (uint)data.Length,
                Data = data,
            },
            cancellationToken);
    }

    /// <summary>Creates an empty file in a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new file's name.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The CREATE result.</returns>
    public ValueTask<Nfs2DirOpResult> CreateAsync(
        Nfs2Handle directory,
        string name,
        Nfs2SetAttributes? attributes = null,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2CreateArgs, Nfs2DirOpResult>(
            Nfs2Procedure.Create,
            new Nfs2CreateArgs
            {
                Where = new Nfs2DirOpArgs { Directory = directory, Name = name },
                Attributes = attributes ?? Nfs2SetAttributes.None,
            },
            cancellationToken);

    /// <summary>Creates a sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new directory's name.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The MKDIR result.</returns>
    public ValueTask<Nfs2DirOpResult> MakeDirectoryAsync(
        Nfs2Handle directory,
        string name,
        Nfs2SetAttributes? attributes = null,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2CreateArgs, Nfs2DirOpResult>(
            Nfs2Procedure.MakeDirectory,
            new Nfs2CreateArgs
            {
                Where = new Nfs2DirOpArgs { Directory = directory, Name = name },
                Attributes = attributes ?? Nfs2SetAttributes.None,
            },
            cancellationToken);

    /// <summary>Removes a file from a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The REMOVE result.</returns>
    public ValueTask<Nfs2StatResult> RemoveAsync(
        Nfs2Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2DirOpArgs, Nfs2StatResult>(
            Nfs2Procedure.Remove,
            new Nfs2DirOpArgs { Directory = directory, Name = name },
            cancellationToken);

    /// <summary>Removes an empty sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The sub-directory's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The RMDIR result.</returns>
    public ValueTask<Nfs2StatResult> RemoveDirectoryAsync(
        Nfs2Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2DirOpArgs, Nfs2StatResult>(
            Nfs2Procedure.RemoveDirectory,
            new Nfs2DirOpArgs { Directory = directory, Name = name },
            cancellationToken);

    /// <summary>Moves or renames an object between directories.</summary>
    /// <param name="sourceDirectory">The directory currently holding the object.</param>
    /// <param name="sourceName">The object's current name.</param>
    /// <param name="targetDirectory">The destination directory.</param>
    /// <param name="targetName">The object's new name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The RENAME result.</returns>
    public ValueTask<Nfs2StatResult> RenameAsync(
        Nfs2Handle sourceDirectory,
        string sourceName,
        Nfs2Handle targetDirectory,
        string targetName,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2RenameArgs, Nfs2StatResult>(
            Nfs2Procedure.Rename,
            new Nfs2RenameArgs
            {
                From = new Nfs2DirOpArgs { Directory = sourceDirectory, Name = sourceName },
                To = new Nfs2DirOpArgs { Directory = targetDirectory, Name = targetName },
            },
            cancellationToken);

    /// <summary>Creates a hard link to an existing object.</summary>
    /// <param name="file">The handle of the existing object to link to.</param>
    /// <param name="directory">The directory that will hold the new link.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LINK result.</returns>
    public ValueTask<Nfs2StatResult> LinkAsync(
        Nfs2Handle file,
        Nfs2Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2LinkArgs, Nfs2StatResult>(
            Nfs2Procedure.Link,
            new Nfs2LinkArgs { From = file, To = new Nfs2DirOpArgs { Directory = directory, Name = name } },
            cancellationToken);

    /// <summary>Creates a symbolic link.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="target">The path the link points to.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The SYMLINK result.</returns>
    public ValueTask<Nfs2StatResult> CreateSymbolicLinkAsync(
        Nfs2Handle directory,
        string name,
        string target,
        Nfs2SetAttributes? attributes = null,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2SymlinkArgs, Nfs2StatResult>(
            Nfs2Procedure.SymbolicLink,
            new Nfs2SymlinkArgs
            {
                From = new Nfs2DirOpArgs { Directory = directory, Name = name },
                Target = target,
                Attributes = attributes ?? Nfs2SetAttributes.None,
            },
            cancellationToken);

    /// <summary>Reads the entries of a directory.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="cookie">A continuation cookie (4 bytes), or all zero to start.</param>
    /// <param name="count">A hint for the maximum number of bytes of entries to return.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READDIR result.</returns>
    public ValueTask<Nfs2ReadDirResult> ReadDirectoryAsync(
        Nfs2Handle directory,
        byte[]? cookie = null,
        uint count = 8192,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2ReadDirArgs, Nfs2ReadDirResult>(
            Nfs2Procedure.ReadDirectory,
            new Nfs2ReadDirArgs { Directory = directory, Cookie = cookie ?? new byte[4], Count = count },
            cancellationToken);

    /// <summary>Gets file-system statistics.</summary>
    /// <param name="root">A handle within the file system (typically the export root).</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The STATFS result.</returns>
    public ValueTask<Nfs2StatFsResult> FileSystemStatusAsync(
        Nfs2Handle root,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs2HandleArgs, Nfs2StatFsResult>(
            Nfs2Procedure.FileSystemStatus, new Nfs2HandleArgs { Handle = root }, cancellationToken);

    private async ValueTask<TResult> CallAsync<TArgs, TResult>(
        Nfs2Procedure procedure,
        TArgs arguments,
        CancellationToken cancellationToken)
        where TArgs : IXdrSerializable<TArgs>
        where TResult : IXdrSerializable<TResult>
    {
        RpcReply reply = await _rpc.CallAsync(
            Nfs2.Program,
            Nfs2.ProtocolVersion,
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
                $"The NFS call was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }
    }
}
