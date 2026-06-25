using Nfs.Protocol.V3;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>
/// A typed NFS version 3 client. It wraps an <see cref="RpcClient"/>, encoding procedure arguments
/// and decoding results for the NFS program (100003, version 3).
/// </summary>
public sealed class Nfs3Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Nfs3Client(IRpcClient rpc, OpaqueAuth credential = default)
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
            Nfs3.Program,
            Nfs3.ProtocolVersion,
            (uint)Nfs3Procedure.Null,
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
    public ValueTask<Nfs3GetAttrResult> GetAttributesAsync(
        Nfs3Handle handle,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3GetAttrArgs, Nfs3GetAttrResult>(
            Nfs3Procedure.GetAttributes,
            new Nfs3GetAttrArgs { Handle = handle },
            cancellationToken);

    /// <summary>Resolves <paramref name="name"/> within the directory <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="name">The name to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LOOKUP result.</returns>
    public ValueTask<Nfs3LookupResult> LookupAsync(
        Nfs3Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3LookupArgs, Nfs3LookupResult>(
            Nfs3Procedure.Lookup,
            new Nfs3LookupArgs { What = new Nfs3DirOpArgs { Directory = directory, Name = name } },
            cancellationToken);

    /// <summary>Reads up to <paramref name="count"/> bytes from a file.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to read from.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READ result.</returns>
    public ValueTask<Nfs3ReadResult> ReadAsync(
        Nfs3Handle file,
        ulong offset,
        uint count,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3ReadArgs, Nfs3ReadResult>(
            Nfs3Procedure.Read,
            new Nfs3ReadArgs { File = file, Offset = offset, Count = count },
            cancellationToken);

    /// <summary>Writes <paramref name="data"/> to a file.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset to write at.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="stable">How durably the server should commit the data.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The WRITE result.</returns>
    public ValueTask<Nfs3WriteResult> WriteAsync(
        Nfs3Handle file,
        ulong offset,
        byte[] data,
        Nfs3StableHow stable = Nfs3StableHow.FileSync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CallAsync<Nfs3WriteArgs, Nfs3WriteResult>(
            Nfs3Procedure.Write,
            new Nfs3WriteArgs
            {
                File = file,
                Offset = offset,
                Count = (uint)data.Length,
                Stable = stable,
                Data = data,
            },
            cancellationToken);
    }

    /// <summary>Checks which of the requested access bits are permitted on an object.</summary>
    /// <param name="handle">The object handle.</param>
    /// <param name="access">The access bits to check (see <see cref="Nfs3Access"/>).</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The ACCESS result.</returns>
    public ValueTask<Nfs3AccessResult> AccessAsync(
        Nfs3Handle handle,
        uint access,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3AccessArgs, Nfs3AccessResult>(
            Nfs3Procedure.Access,
            new Nfs3AccessArgs { Handle = handle, Access = access },
            cancellationToken);

    /// <summary>Gets static information about the file system containing a handle.</summary>
    /// <param name="root">A handle within the file system (typically the export root).</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The FSINFO result.</returns>
    public ValueTask<Nfs3FsInfoResult> FileSystemInfoAsync(
        Nfs3Handle root,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3FsInfoArgs, Nfs3FsInfoResult>(
            Nfs3Procedure.FileSystemInfo,
            new Nfs3FsInfoArgs { FileSystemRoot = root },
            cancellationToken);

    /// <summary>Creates an empty file in a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new file's name.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The CREATE result.</returns>
    public ValueTask<Nfs3CreateResult> CreateAsync(
        Nfs3Handle directory,
        string name,
        Nfs3SetAttributes attributes = default,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3CreateArgs, Nfs3CreateResult>(
            Nfs3Procedure.Create,
            new Nfs3CreateArgs
            {
                Where = new Nfs3DirOpArgs { Directory = directory, Name = name },
                How = Nfs3CreateHow.CreateUnchecked(attributes),
            },
            cancellationToken);

    /// <summary>Creates a sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new directory's name.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The MKDIR result.</returns>
    public ValueTask<Nfs3CreateResult> MakeDirectoryAsync(
        Nfs3Handle directory,
        string name,
        Nfs3SetAttributes attributes = default,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3MkdirArgs, Nfs3CreateResult>(
            Nfs3Procedure.MakeDirectory,
            new Nfs3MkdirArgs
            {
                Where = new Nfs3DirOpArgs { Directory = directory, Name = name },
                Attributes = attributes,
            },
            cancellationToken);

    /// <summary>Creates a special node.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new node's name.</param>
    /// <param name="data">The node type-specific MKNOD data.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The MKNOD result.</returns>
    public ValueTask<Nfs3CreateResult> MakeNodeAsync(
        Nfs3Handle directory,
        string name,
        Nfs3MknodData data,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3MknodArgs, Nfs3CreateResult>(
            Nfs3Procedure.MakeNode,
            new Nfs3MknodArgs
            {
                Where = new Nfs3DirOpArgs { Directory = directory, Name = name },
                Data = data,
            },
            cancellationToken);

    /// <summary>Removes a file from a directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The file's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The REMOVE result.</returns>
    public ValueTask<Nfs3WccResult> RemoveAsync(
        Nfs3Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3RemoveArgs, Nfs3WccResult>(
            Nfs3Procedure.Remove,
            new Nfs3RemoveArgs { Target = new Nfs3DirOpArgs { Directory = directory, Name = name } },
            cancellationToken);

    /// <summary>Removes an empty sub-directory.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The sub-directory's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The RMDIR result.</returns>
    public ValueTask<Nfs3WccResult> RemoveDirectoryAsync(
        Nfs3Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3RemoveArgs, Nfs3WccResult>(
            Nfs3Procedure.RemoveDirectory,
            new Nfs3RemoveArgs { Target = new Nfs3DirOpArgs { Directory = directory, Name = name } },
            cancellationToken);

    /// <summary>Reads the entries of a directory.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="cookie">A continuation cookie, or 0 to start at the beginning.</param>
    /// <param name="count">A hint for the maximum number of bytes of entries to return.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READDIR result.</returns>
    public ValueTask<Nfs3ReadDirResult> ReadDirectoryAsync(
        Nfs3Handle directory,
        ulong cookie = 0,
        uint count = 8192,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3ReadDirArgs, Nfs3ReadDirResult>(
            Nfs3Procedure.ReadDirectory,
            new Nfs3ReadDirArgs
            {
                Directory = directory,
                Cookie = cookie,
                CookieVerifier = new byte[8],
                Count = count,
            },
            cancellationToken);

    /// <summary>Applies a set of attribute changes to an object.</summary>
    /// <param name="handle">The object's handle.</param>
    /// <param name="attributes">The attributes to set.</param>
    /// <param name="guardChangeTime">An optional change-time the object must currently have.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The SETATTR result.</returns>
    public ValueTask<Nfs3WccResult> SetAttributesAsync(
        Nfs3Handle handle,
        Nfs3SetAttributes attributes,
        Nfs3Time? guardChangeTime = null,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3SetAttrArgs, Nfs3WccResult>(
            Nfs3Procedure.SetAttributes,
            new Nfs3SetAttrArgs
            {
                Handle = handle,
                Attributes = attributes,
                Guard = new Nfs3SetAttrGuard { ObjectChangeTime = guardChangeTime },
            },
            cancellationToken);

    /// <summary>Moves or renames an object between directories.</summary>
    /// <param name="sourceDirectory">The directory currently holding the object.</param>
    /// <param name="sourceName">The object's current name.</param>
    /// <param name="targetDirectory">The destination directory.</param>
    /// <param name="targetName">The object's new name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The RENAME result.</returns>
    public ValueTask<Nfs3RenameResult> RenameAsync(
        Nfs3Handle sourceDirectory,
        string sourceName,
        Nfs3Handle targetDirectory,
        string targetName,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3RenameArgs, Nfs3RenameResult>(
            Nfs3Procedure.Rename,
            new Nfs3RenameArgs
            {
                From = new Nfs3DirOpArgs { Directory = sourceDirectory, Name = sourceName },
                To = new Nfs3DirOpArgs { Directory = targetDirectory, Name = targetName },
            },
            cancellationToken);

    /// <summary>Creates a symbolic link.</summary>
    /// <param name="directory">The parent directory handle.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="target">The path the link points to.</param>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The SYMLINK result.</returns>
    public ValueTask<Nfs3CreateResult> CreateSymbolicLinkAsync(
        Nfs3Handle directory,
        string name,
        string target,
        Nfs3SetAttributes attributes = default,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3SymlinkArgs, Nfs3CreateResult>(
            Nfs3Procedure.SymbolicLink,
            new Nfs3SymlinkArgs
            {
                Where = new Nfs3DirOpArgs { Directory = directory, Name = name },
                Attributes = attributes,
                Target = target,
            },
            cancellationToken);

    /// <summary>Reads the target path of a symbolic link.</summary>
    /// <param name="handle">The link's handle.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READLINK result.</returns>
    public ValueTask<Nfs3ReadLinkResult> ReadSymbolicLinkAsync(
        Nfs3Handle handle,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3ReadLinkArgs, Nfs3ReadLinkResult>(
            Nfs3Procedure.ReadLink,
            new Nfs3ReadLinkArgs { SymbolicLink = handle },
            cancellationToken);

    /// <summary>Creates a hard link to an existing object.</summary>
    /// <param name="file">The handle of the existing object to link to.</param>
    /// <param name="directory">The directory that will hold the new link.</param>
    /// <param name="name">The new link's name.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LINK result.</returns>
    public ValueTask<Nfs3LinkResult> LinkAsync(
        Nfs3Handle file,
        Nfs3Handle directory,
        string name,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3LinkArgs, Nfs3LinkResult>(
            Nfs3Procedure.Link,
            new Nfs3LinkArgs
            {
                File = file,
                Link = new Nfs3DirOpArgs { Directory = directory, Name = name },
            },
            cancellationToken);

    /// <summary>Reads the entries of a directory together with each entry's attributes and handle.</summary>
    /// <param name="directory">The directory handle.</param>
    /// <param name="cookie">A continuation cookie, or 0 to start at the beginning.</param>
    /// <param name="directoryCount">A hint for the maximum bytes of directory information to return.</param>
    /// <param name="maxCount">A hint for the maximum bytes in the whole reply.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The READDIRPLUS result.</returns>
    public ValueTask<Nfs3ReadDirPlusResult> ReadDirectoryPlusAsync(
        Nfs3Handle directory,
        ulong cookie = 0,
        uint directoryCount = 8192,
        uint maxCount = 32768,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3ReadDirPlusArgs, Nfs3ReadDirPlusResult>(
            Nfs3Procedure.ReadDirectoryPlus,
            new Nfs3ReadDirPlusArgs
            {
                Directory = directory,
                Cookie = cookie,
                CookieVerifier = new byte[8],
                DirectoryCount = directoryCount,
                MaxCount = maxCount,
            },
            cancellationToken);

    /// <summary>Gets dynamic information about the file system containing a handle.</summary>
    /// <param name="root">A handle within the file system (typically the export root).</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The FSSTAT result.</returns>
    public ValueTask<Nfs3FsStatResult> FileSystemStatusAsync(
        Nfs3Handle root,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3FsStatArgs, Nfs3FsStatResult>(
            Nfs3Procedure.FileSystemStatus,
            new Nfs3FsStatArgs { FileSystemRoot = root },
            cancellationToken);

    /// <summary>Retrieves POSIX path information for an object.</summary>
    /// <param name="handle">The object handle.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The PATHCONF result.</returns>
    public ValueTask<Nfs3PathConfResult> PathConfigurationAsync(
        Nfs3Handle handle,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3PathConfArgs, Nfs3PathConfResult>(
            Nfs3Procedure.PathConfiguration,
            new Nfs3PathConfArgs { Handle = handle },
            cancellationToken);

    /// <summary>Asks the server to commit previously written data to stable storage.</summary>
    /// <param name="file">The file handle.</param>
    /// <param name="offset">The byte offset at which the flush begins (0 means the whole file).</param>
    /// <param name="count">The number of bytes to flush (0 means to the end of the file).</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The COMMIT result.</returns>
    public ValueTask<Nfs3CommitResult> CommitAsync(
        Nfs3Handle file,
        ulong offset = 0,
        uint count = 0,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nfs3CommitArgs, Nfs3CommitResult>(
            Nfs3Procedure.Commit,
            new Nfs3CommitArgs { File = file, Offset = offset, Count = count },
            cancellationToken);

    private async ValueTask<TResult> CallAsync<TArgs, TResult>(
        Nfs3Procedure procedure,
        TArgs arguments,
        CancellationToken cancellationToken)
        where TArgs : IXdrSerializable<TArgs>
        where TResult : IXdrSerializable<TResult>
    {
        RpcReply reply = await _rpc.CallAsync(
            Nfs3.Program,
            Nfs3.ProtocolVersion,
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
