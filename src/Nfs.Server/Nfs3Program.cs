using System.Buffers;
using System.Security.Cryptography;

using Nfs.Abstractions;
using Nfs.Protocol.V3;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the NFS version 3 program (100003) by dispatching
/// procedures to a pluggable <see cref="INfsFileSystem"/>. Host it with an
/// <see cref="RpcServer"/>.
/// </summary>
public sealed class Nfs3Program : IRpcProgram
{
    private readonly INfsFileSystem _fileSystem;
    private readonly byte[] _writeVerifier = RandomNumberGenerator.GetBytes(8);

    /// <summary>Creates a handler backed by <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The storage backend to serve.</param>
    public Nfs3Program(INfsFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc/>
    public uint Program => Nfs3.Program;

    /// <inheritdoc/>
    public async ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Nfs3.ProtocolVersion)
        {
            return RpcReplyPayload.ProgramMismatch(Nfs3.ProtocolVersion, Nfs3.ProtocolVersion);
        }

        return (Nfs3Procedure)request.Procedure switch
        {
            Nfs3Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nfs3Procedure.GetAttributes => await GetAttributesAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.SetAttributes => await SetAttributesAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Lookup => await LookupAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Read => await ReadAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Write => await WriteAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Access => await AccessAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.FileSystemInfo => await FileSystemInfoAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Create => await CreateAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.MakeDirectory => await MakeDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.MakeNode => await MakeNodeAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Remove => await RemoveAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.RemoveDirectory => await RemoveDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Rename => await RenameAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.SymbolicLink => await SymbolicLinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.ReadLink => await ReadLinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Link => await LinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.ReadDirectory => await ReadDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.ReadDirectoryPlus => await ReadDirectoryPlusAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.FileSystemStatus => await FileSystemStatusAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.PathConfiguration => await PathConfigurationAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs3Procedure.Commit => await CommitAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };
    }

    private async ValueTask<RpcReplyPayload> GetAttributesAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3Handle handle = DecodeGetAttr(arguments).Handle;

        Nfs3GetAttrResult result;
        try
        {
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(Nfs3Mapping.ToHandle(handle), cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3GetAttrResult.Success(Nfs3Mapping.ToWire(attributes));
        }
        catch (NfsException ex)
        {
            result = Nfs3GetAttrResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> LookupAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3DirOpArgs what = DecodeLookup(arguments).What;

        Nfs3LookupResult result;
        try
        {
            NfsFileHandle child = await _fileSystem
                .LookupAsync(Nfs3Mapping.ToHandle(what.Directory), what.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3LookupResult.Success(new Nfs3LookupResultOk { Handle = Nfs3Mapping.ToWire(child) });
        }
        catch (NfsException ex)
        {
            result = Nfs3LookupResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> ReadAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3ReadArgs args = DecodeRead(arguments);

        try
        {
            using var data = new PooledBufferWriter((int)Math.Min(args.Count, (uint)Nfs3.MaxReadSize));
            NfsBufferedReadResult read = await _fileSystem
                .ReadAsync(Nfs3Mapping.ToHandle(args.File), args.Offset, args.Count, data, cancellationToken)
                .ConfigureAwait(false);
            return EncodeReadSuccess(read, data.WrittenSpan);
        }
        catch (NfsException ex)
        {
            return Encode(Nfs3ReadResult.Failure(ex.Status));
        }
    }

    private async ValueTask<RpcReplyPayload> WriteAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3WriteRequest args = DecodeWrite(arguments);

        Nfs3WriteResult result;
        try
        {
            NfsWriteResult write = await _fileSystem
                .WriteAsync(Nfs3Mapping.ToHandle(args.File), args.Offset, args.Data, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3WriteResult.Success(new Nfs3WriteResultOk
            {
                Count = write.Count,
                Committed = Nfs3StableHow.FileSync,
                Verifier = _writeVerifier,
            });
        }
        catch (NfsException ex)
        {
            result = Nfs3WriteResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> AccessAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3AccessArgs args = DecodeAccess(arguments);

        Nfs3AccessResult result;
        try
        {
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(Nfs3Mapping.ToHandle(args.Handle), cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3AccessResult.Success(new Nfs3AccessResultOk
            {
                ObjectAttributes = Nfs3Mapping.ToWire(attributes),
                Access = args.Access & Nfs3Access.All,
            });
        }
        catch (NfsException ex)
        {
            result = Nfs3AccessResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> FileSystemInfoAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3FsInfoArgs args = DecodeFsInfo(arguments);

        Nfs3FileAttributes? attributes = null;
        try
        {
            NfsFileAttributes rootAttributes = await _fileSystem
                .GetAttributesAsync(Nfs3Mapping.ToHandle(args.FileSystemRoot), cancellationToken)
                .ConfigureAwait(false);
            attributes = Nfs3Mapping.ToWire(rootAttributes);
        }
        catch (NfsException)
        {
            // post_op_attr is optional; omit it if the root cannot be read.
        }

        var result = Nfs3FsInfoResult.Success(new Nfs3FsInfoResultOk
        {
            Attributes = attributes,
            ReadMax = Nfs3.MaxReadSize,
            ReadPreferred = Nfs3.MaxReadSize,
            ReadMultiple = 4096,
            WriteMax = Nfs3.MaxWriteSize,
            WritePreferred = Nfs3.MaxWriteSize,
            WriteMultiple = 4096,
            DirectoryPreferred = 8192,
            MaxFileSize = (ulong)long.MaxValue,
            TimeDelta = new Nfs3Time { Seconds = 0, Nanoseconds = 1 },
            Properties = Nfs3FsProperties.Link | Nfs3FsProperties.SymbolicLink
                | Nfs3FsProperties.Homogeneous | Nfs3FsProperties.CanSetTime,
        });

        return Encode(result);
    }

    private static Nfs3GetAttrArgs DecodeGetAttr(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3GetAttrArgs.ReadFrom(ref reader);
    }

    private async ValueTask<RpcReplyPayload> CreateAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3CreateArgs args = DecodeCreate(arguments);

        Nfs3CreateResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .CreateAsync(Nfs3Mapping.ToHandle(args.Where.Directory), args.Where.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3CreateResult.Success(await BuildCreateOkAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3CreateResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> MakeDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3MkdirArgs args = DecodeMkdir(arguments);

        Nfs3CreateResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .MakeDirectoryAsync(Nfs3Mapping.ToHandle(args.Where.Directory), args.Where.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3CreateResult.Success(await BuildCreateOkAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3CreateResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> MakeNodeAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3MknodArgs args = DecodeMknod(arguments);

        Nfs3CreateResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .MakeSpecialNodeAsync(
                    Nfs3Mapping.ToHandle(args.Where.Directory),
                    args.Where.Name,
                    args.Data.Type,
                    cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3CreateResult.Success(await BuildCreateOkAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3CreateResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> RemoveAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3RemoveArgs args = DecodeRemove(arguments);

        Nfs3WccResult result;
        try
        {
            await _fileSystem
                .RemoveAsync(Nfs3Mapping.ToHandle(args.Target.Directory), args.Target.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3WccResult.Success();
        }
        catch (NfsException ex)
        {
            result = Nfs3WccResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> RemoveDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3RemoveArgs args = DecodeRemove(arguments);

        Nfs3WccResult result;
        try
        {
            await _fileSystem
                .RemoveDirectoryAsync(Nfs3Mapping.ToHandle(args.Target.Directory), args.Target.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3WccResult.Success();
        }
        catch (NfsException ex)
        {
            result = Nfs3WccResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<Nfs3CreateResultOk> BuildCreateOkAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken)
    {
        Nfs3FileAttributes? attributes = null;
        try
        {
            attributes = Nfs3Mapping.ToWire(await _fileSystem.GetAttributesAsync(handle, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (NfsException)
        {
            // post_op_attr is optional.
        }

        return new Nfs3CreateResultOk { Handle = Nfs3Mapping.ToWire(handle), Attributes = attributes };
    }

    private static Nfs3CreateArgs DecodeCreate(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3CreateArgs.ReadFrom(ref reader);
    }

    private static Nfs3MkdirArgs DecodeMkdir(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3MkdirArgs.ReadFrom(ref reader);
    }

    private static Nfs3MknodArgs DecodeMknod(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3MknodArgs.ReadFrom(ref reader);
    }

    private static Nfs3RemoveArgs DecodeRemove(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3RemoveArgs.ReadFrom(ref reader);
    }

    private async ValueTask<RpcReplyPayload> ReadDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3ReadDirArgs args = DecodeReadDir(arguments);

        Nfs3ReadDirResult result;
        try
        {
            NfsFileHandle handle = Nfs3Mapping.ToHandle(args.Directory);
            NfsDirectoryListing listing = await _fileSystem
                .ReadDirectoryAsync(handle, args.Cookie, args.Count, cancellationToken)
                .ConfigureAwait(false);

            var entries = new Nfs3DirEntry[listing.Entries.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                NfsDirectoryEntry entry = listing.Entries[i];
                entries[i] = new Nfs3DirEntry(entry.FileId, entry.Name, entry.Cookie);
            }

            Nfs3FileAttributes? directoryAttributes = null;
            try
            {
                directoryAttributes = Nfs3Mapping.ToWire(
                    await _fileSystem.GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false));
            }
            catch (NfsException)
            {
                // post_op_attr is optional.
            }

            result = Nfs3ReadDirResult.Success(new Nfs3ReadDirResultOk
            {
                DirectoryAttributes = directoryAttributes,
                CookieVerifier = new byte[8],
                Entries = entries,
                Eof = listing.EndOfStream,
            });
        }
        catch (NfsException ex)
        {
            result = Nfs3ReadDirResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private static Nfs3ReadDirArgs DecodeReadDir(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3ReadDirArgs.ReadFrom(ref reader);
    }

    private static Nfs3LookupArgs DecodeLookup(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3LookupArgs.ReadFrom(ref reader);
    }

    private static Nfs3ReadArgs DecodeRead(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3ReadArgs.ReadFrom(ref reader);
    }

    private static Nfs3WriteRequest DecodeWrite(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        Nfs3Handle file = Nfs3Handle.ReadFrom(ref reader);
        ulong offset = reader.ReadUInt64();
        _ = reader.ReadUInt32();
        _ = reader.ReadInt32();
        int length = reader.ReadLength(Nfs3.MaxWriteSize);
        int dataOffset = reader.Position;
        _ = reader.ReadOpaqueFixed(length);
        return new Nfs3WriteRequest(file, offset, arguments.Slice(dataOffset, length));
    }

    private static Nfs3AccessArgs DecodeAccess(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3AccessArgs.ReadFrom(ref reader);
    }

    private readonly record struct Nfs3WriteRequest(
        Nfs3Handle File,
        ulong Offset,
        ReadOnlyMemory<byte> Data);

    private static Nfs3FsInfoArgs DecodeFsInfo(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3FsInfoArgs.ReadFrom(ref reader);
    }

    private async ValueTask<RpcReplyPayload> SetAttributesAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3SetAttrArgs args = DecodeSetAttr(arguments);

        Nfs3WccResult result;
        try
        {
            await _fileSystem
                .SetAttributesAsync(
                    Nfs3Mapping.ToHandle(args.Handle),
                    Nfs3Mapping.ToSetAttributes(args.Attributes),
                    cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3WccResult.Success();
        }
        catch (NfsException ex)
        {
            result = Nfs3WccResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> RenameAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3RenameArgs args = DecodeRename(arguments);

        Nfs3RenameResult result;
        try
        {
            await _fileSystem
                .RenameAsync(
                    Nfs3Mapping.ToHandle(args.From.Directory),
                    args.From.Name,
                    Nfs3Mapping.ToHandle(args.To.Directory),
                    args.To.Name,
                    cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3RenameResult.Success();
        }
        catch (NfsException ex)
        {
            result = Nfs3RenameResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> SymbolicLinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3SymlinkArgs args = DecodeSymlink(arguments);

        Nfs3CreateResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .CreateSymbolicLinkAsync(
                    Nfs3Mapping.ToHandle(args.Where.Directory),
                    args.Where.Name,
                    args.Target,
                    cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3CreateResult.Success(await BuildCreateOkAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3CreateResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> ReadLinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3Handle wire = DecodeReadLink(arguments).SymbolicLink;

        Nfs3ReadLinkResult result;
        try
        {
            NfsFileHandle handle = Nfs3Mapping.ToHandle(wire);
            string target = await _fileSystem.ReadSymbolicLinkAsync(handle, cancellationToken).ConfigureAwait(false);
            result = Nfs3ReadLinkResult.Success(
                target, await TryGetWireAttributesAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3ReadLinkResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> LinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3LinkArgs args = DecodeLink(arguments);

        Nfs3LinkResult result;
        try
        {
            NfsFileHandle target = Nfs3Mapping.ToHandle(args.File);
            await _fileSystem
                .CreateHardLinkAsync(
                    target, Nfs3Mapping.ToHandle(args.Link.Directory), args.Link.Name, cancellationToken)
                .ConfigureAwait(false);
            result = Nfs3LinkResult.Success(
                await TryGetWireAttributesAsync(target, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException ex)
        {
            result = Nfs3LinkResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> ReadDirectoryPlusAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3ReadDirPlusArgs args = DecodeReadDirPlus(arguments);

        Nfs3ReadDirPlusResult result;
        try
        {
            NfsFileHandle handle = Nfs3Mapping.ToHandle(args.Directory);
            NfsDirectoryListing listing = await _fileSystem
                .ReadDirectoryAsync(handle, args.Cookie, args.DirectoryCount, cancellationToken)
                .ConfigureAwait(false);

            var entries = new Nfs3DirEntryPlus[listing.Entries.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                NfsDirectoryEntry entry = listing.Entries[i];
                Nfs3Handle? childHandle = null;
                Nfs3FileAttributes? childAttributes = null;
                try
                {
                    NfsFileHandle child = await _fileSystem
                        .LookupAsync(handle, entry.Name, cancellationToken).ConfigureAwait(false);
                    childHandle = Nfs3Mapping.ToWire(child);
                    childAttributes = await TryGetWireAttributesAsync(child, cancellationToken).ConfigureAwait(false);
                }
                catch (NfsException)
                {
                    // name_handle and name_attributes are both optional.
                }

                entries[i] = new Nfs3DirEntryPlus(entry.FileId, entry.Name, entry.Cookie, childAttributes, childHandle);
            }

            result = Nfs3ReadDirPlusResult.Success(new Nfs3ReadDirPlusResultOk
            {
                DirectoryAttributes = await TryGetWireAttributesAsync(handle, cancellationToken).ConfigureAwait(false),
                CookieVerifier = new byte[8],
                Entries = entries,
                Eof = listing.EndOfStream,
            });
        }
        catch (NfsException ex)
        {
            result = Nfs3ReadDirPlusResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> FileSystemStatusAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3Handle wire = DecodeFsStat(arguments).FileSystemRoot;

        Nfs3FsStatResult result;
        try
        {
            NfsFileHandle handle = Nfs3Mapping.ToHandle(wire);
            NfsFileSystemStats stats = await _fileSystem
                .GetFileSystemStatsAsync(handle, cancellationToken).ConfigureAwait(false);
            result = new Nfs3FsStatResult
            {
                Status = NfsStatus.Ok,
                Attributes = await TryGetWireAttributesAsync(handle, cancellationToken).ConfigureAwait(false),
                TotalBytes = stats.TotalBytes,
                FreeBytes = stats.FreeBytes,
                AvailableBytes = stats.AvailableBytes,
                TotalFiles = stats.TotalFiles,
                FreeFiles = stats.FreeFiles,
                AvailableFiles = stats.AvailableFiles,
                InvariantSeconds = stats.InvariantSeconds,
            };
        }
        catch (NfsException ex)
        {
            result = Nfs3FsStatResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> PathConfigurationAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3Handle wire = DecodePathConf(arguments).Handle;

        Nfs3PathConfResult result;
        try
        {
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(Nfs3Mapping.ToHandle(wire), cancellationToken).ConfigureAwait(false);
            result = new Nfs3PathConfResult
            {
                Status = NfsStatus.Ok,
                Attributes = Nfs3Mapping.ToWire(attributes),
                LinkMax = 32000,
                NameMax = Nfs3.MaxNameLength,
                NoTruncate = true,
                ChownRestricted = true,
                CaseInsensitive = false,
                CasePreserving = true,
            };
        }
        catch (NfsException ex)
        {
            result = Nfs3PathConfResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> CommitAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs3CommitArgs args = DecodeCommit(arguments);

        Nfs3CommitResult result;
        try
        {
            // The provided backends write synchronously, so data is already stable; just validate the handle.
            _ = await _fileSystem
                .GetAttributesAsync(Nfs3Mapping.ToHandle(args.File), cancellationToken).ConfigureAwait(false);
            result = Nfs3CommitResult.Success(_writeVerifier);
        }
        catch (NfsException ex)
        {
            result = Nfs3CommitResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<Nfs3FileAttributes?> TryGetWireAttributesAsync(
        NfsFileHandle handle,
        CancellationToken cancellationToken)
    {
        try
        {
            return Nfs3Mapping.ToWire(
                await _fileSystem.GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false));
        }
        catch (NfsException)
        {
            return null;
        }
    }

    private static Nfs3SetAttrArgs DecodeSetAttr(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3SetAttrArgs.ReadFrom(ref reader);
    }

    private static Nfs3RenameArgs DecodeRename(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3RenameArgs.ReadFrom(ref reader);
    }

    private static Nfs3SymlinkArgs DecodeSymlink(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3SymlinkArgs.ReadFrom(ref reader);
    }

    private static Nfs3ReadLinkArgs DecodeReadLink(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3ReadLinkArgs.ReadFrom(ref reader);
    }

    private static Nfs3LinkArgs DecodeLink(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3LinkArgs.ReadFrom(ref reader);
    }

    private static Nfs3ReadDirPlusArgs DecodeReadDirPlus(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3ReadDirPlusArgs.ReadFrom(ref reader);
    }

    private static Nfs3FsStatArgs DecodeFsStat(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3FsStatArgs.ReadFrom(ref reader);
    }

    private static Nfs3PathConfArgs DecodePathConf(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3PathConfArgs.ReadFrom(ref reader);
    }

    private static Nfs3CommitArgs DecodeCommit(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs3CommitArgs.ReadFrom(ref reader);
    }

    private static RpcReplyPayload Encode<T>(T result)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }

    private static RpcReplyPayload EncodeReadSuccess(NfsBufferedReadResult read, ReadOnlySpan<byte> data)
    {
        var buffer = new ArrayBufferWriter<byte>(data.Length + 24);
        var writer = new XdrWriter(buffer);
        writer.WriteInt32((int)NfsStatus.Ok);
        writer.WriteBool(false);
        writer.WriteUInt32((uint)data.Length);
        writer.WriteBool(read.EndOfFile);
        writer.WriteOpaqueVariable(data);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }
}
