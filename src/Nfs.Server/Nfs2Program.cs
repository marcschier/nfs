using System.Buffers;

using Nfs.Abstractions;
using Nfs.Protocol.V2;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the NFS version 2 program (100003, version 2) by
/// dispatching procedures to a pluggable <see cref="INfsFileSystem"/>.
/// </summary>
public sealed class Nfs2Program : IRpcProgram
{
    private readonly INfsFileSystem _fileSystem;

    /// <summary>Creates a handler backed by <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The storage backend to serve.</param>
    public Nfs2Program(INfsFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc/>
    public uint Program => Nfs2.Program;

    /// <inheritdoc/>
    public async ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Nfs2.ProtocolVersion)
        {
            return RpcReplyPayload.ProgramMismatch(Nfs2.ProtocolVersion, Nfs2.ProtocolVersion);
        }

        return (Nfs2Procedure)request.Procedure switch
        {
            Nfs2Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nfs2Procedure.GetAttributes => await GetAttributesAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.SetAttributes => await SetAttributesAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Lookup => await LookupAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.ReadLink => await ReadLinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Read => await ReadAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Write => await WriteAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Create => await CreateAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Remove => await RemoveAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Rename => await RenameAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.Link => await LinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.SymbolicLink => await SymbolicLinkAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.MakeDirectory => await MakeDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.RemoveDirectory => await RemoveDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.ReadDirectory => await ReadDirectoryAsync(arguments, cancellationToken).ConfigureAwait(false),
            Nfs2Procedure.FileSystemStatus => await FileSystemStatusAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };
    }

    private async ValueTask<RpcReplyPayload> GetAttributesAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2Handle wire = Decode<Nfs2HandleArgs>(arguments).Handle;

        Nfs2AttrStat result;
        try
        {
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(Nfs2Mapping.ToHandle(wire), cancellationToken).ConfigureAwait(false);
            result = Nfs2AttrStat.Success(Nfs2Mapping.ToWire(attributes));
        }
        catch (NfsException ex)
        {
            result = Nfs2AttrStat.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> SetAttributesAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2SetAttrArgs args = Decode<Nfs2SetAttrArgs>(arguments);

        Nfs2AttrStat result;
        try
        {
            NfsFileAttributes attributes = await _fileSystem
                .SetAttributesAsync(
                    Nfs2Mapping.ToHandle(args.Handle), Nfs2Mapping.ToSetAttributes(args.Attributes), cancellationToken)
                .ConfigureAwait(false);
            result = Nfs2AttrStat.Success(Nfs2Mapping.ToWire(attributes));
        }
        catch (NfsException ex)
        {
            result = Nfs2AttrStat.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> LookupAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2DirOpArgs what = Decode<Nfs2DirOpArgs>(arguments);

        Nfs2DirOpResult result;
        try
        {
            NfsFileHandle child = await _fileSystem
                .LookupAsync(Nfs2Mapping.ToHandle(what.Directory), what.Name, cancellationToken).ConfigureAwait(false);
            result = await BuildDirOpAsync(child, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            result = Nfs2DirOpResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> ReadLinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2Handle wire = Decode<Nfs2HandleArgs>(arguments).Handle;

        Nfs2ReadLinkResult result;
        try
        {
            string target = await _fileSystem
                .ReadSymbolicLinkAsync(Nfs2Mapping.ToHandle(wire), cancellationToken).ConfigureAwait(false);
            result = Nfs2ReadLinkResult.Success(target);
        }
        catch (NfsException ex)
        {
            result = Nfs2ReadLinkResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> ReadAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2ReadArgs args = Decode<Nfs2ReadArgs>(arguments);

        try
        {
            NfsFileHandle handle = Nfs2Mapping.ToHandle(args.File);
            uint count = Math.Min(args.Count, Nfs2.MaxData);
            using var data = new PooledBufferWriter((int)count);
            _ = await _fileSystem
                .ReadAsync(handle, args.Offset, count, data, cancellationToken).ConfigureAwait(false);
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false);
            return EncodeReadSuccess(Nfs2Mapping.ToWire(attributes), data.WrittenSpan);
        }
        catch (NfsException ex)
        {
            return Encode(Nfs2ReadResult.Failure(ex.Status));
        }
    }

    private async ValueTask<RpcReplyPayload> WriteAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2WriteRequest args = DecodeWrite(arguments);

        Nfs2AttrStat result;
        try
        {
            NfsFileHandle handle = Nfs2Mapping.ToHandle(args.File);
            await _fileSystem.WriteAsync(handle, args.Offset, args.Data, cancellationToken).ConfigureAwait(false);
            NfsFileAttributes attributes = await _fileSystem
                .GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false);
            result = Nfs2AttrStat.Success(Nfs2Mapping.ToWire(attributes));
        }
        catch (NfsException ex)
        {
            result = Nfs2AttrStat.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> CreateAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2CreateArgs args = Decode<Nfs2CreateArgs>(arguments);

        Nfs2DirOpResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .CreateAsync(Nfs2Mapping.ToHandle(args.Where.Directory), args.Where.Name, cancellationToken)
                .ConfigureAwait(false);
            await ApplyInitialAttributesAsync(handle, args.Attributes, cancellationToken).ConfigureAwait(false);
            result = await BuildDirOpAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            result = Nfs2DirOpResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> MakeDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2CreateArgs args = Decode<Nfs2CreateArgs>(arguments);

        Nfs2DirOpResult result;
        try
        {
            NfsFileHandle handle = await _fileSystem
                .MakeDirectoryAsync(Nfs2Mapping.ToHandle(args.Where.Directory), args.Where.Name, cancellationToken)
                .ConfigureAwait(false);
            await ApplyInitialAttributesAsync(handle, args.Attributes, cancellationToken).ConfigureAwait(false);
            result = await BuildDirOpAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            result = Nfs2DirOpResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> RemoveAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2DirOpArgs args = Decode<Nfs2DirOpArgs>(arguments);

        NfsStatus status = NfsStatus.Ok;
        try
        {
            await _fileSystem
                .RemoveAsync(Nfs2Mapping.ToHandle(args.Directory), args.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            status = ex.Status;
        }

        return Encode(Nfs2StatResult.Create(status));
    }

    private async ValueTask<RpcReplyPayload> RemoveDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2DirOpArgs args = Decode<Nfs2DirOpArgs>(arguments);

        NfsStatus status = NfsStatus.Ok;
        try
        {
            await _fileSystem
                .RemoveDirectoryAsync(Nfs2Mapping.ToHandle(args.Directory), args.Name, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            status = ex.Status;
        }

        return Encode(Nfs2StatResult.Create(status));
    }

    private async ValueTask<RpcReplyPayload> RenameAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2RenameArgs args = Decode<Nfs2RenameArgs>(arguments);

        NfsStatus status = NfsStatus.Ok;
        try
        {
            await _fileSystem
                .RenameAsync(
                    Nfs2Mapping.ToHandle(args.From.Directory),
                    args.From.Name,
                    Nfs2Mapping.ToHandle(args.To.Directory),
                    args.To.Name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            status = ex.Status;
        }

        return Encode(Nfs2StatResult.Create(status));
    }

    private async ValueTask<RpcReplyPayload> LinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2LinkArgs args = Decode<Nfs2LinkArgs>(arguments);

        NfsStatus status = NfsStatus.Ok;
        try
        {
            await _fileSystem
                .CreateHardLinkAsync(
                    Nfs2Mapping.ToHandle(args.From),
                    Nfs2Mapping.ToHandle(args.To.Directory),
                    args.To.Name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            status = ex.Status;
        }

        return Encode(Nfs2StatResult.Create(status));
    }

    private async ValueTask<RpcReplyPayload> SymbolicLinkAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2SymlinkArgs args = Decode<Nfs2SymlinkArgs>(arguments);

        NfsStatus status = NfsStatus.Ok;
        try
        {
            await _fileSystem
                .CreateSymbolicLinkAsync(
                    Nfs2Mapping.ToHandle(args.From.Directory), args.From.Name, args.Target, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NfsException ex)
        {
            status = ex.Status;
        }

        return Encode(Nfs2StatResult.Create(status));
    }

    private async ValueTask<RpcReplyPayload> ReadDirectoryAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2ReadDirArgs args = Decode<Nfs2ReadDirArgs>(arguments);

        Nfs2ReadDirResult result;
        try
        {
            NfsDirectoryListing listing = await _fileSystem
                .ReadDirectoryAsync(
                    Nfs2Mapping.ToHandle(args.Directory),
                    Nfs2Mapping.FromCookie(args.Cookie),
                    args.Count,
                    cancellationToken)
                .ConfigureAwait(false);

            var entries = new Nfs2DirEntry[listing.Entries.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                NfsDirectoryEntry entry = listing.Entries[i];
                entries[i] = new Nfs2DirEntry(
                    (uint)entry.FileId, entry.Name, Nfs2Mapping.ToCookie((uint)entry.Cookie));
            }

            result = Nfs2ReadDirResult.Success(entries, listing.EndOfStream);
        }
        catch (NfsException ex)
        {
            result = Nfs2ReadDirResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask<RpcReplyPayload> FileSystemStatusAsync(
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs2Handle wire = Decode<Nfs2HandleArgs>(arguments).Handle;

        Nfs2StatFsResult result;
        try
        {
            NfsFileSystemStats stats = await _fileSystem
                .GetFileSystemStatsAsync(Nfs2Mapping.ToHandle(wire), cancellationToken).ConfigureAwait(false);
            const uint blockSize = 4096;
            result = new Nfs2StatFsResult
            {
                Status = NfsStatus.Ok,
                TransferSize = Nfs2.MaxData,
                BlockSize = blockSize,
                TotalBlocks = (uint)Math.Min(stats.TotalBytes / blockSize, uint.MaxValue),
                FreeBlocks = (uint)Math.Min(stats.FreeBytes / blockSize, uint.MaxValue),
                AvailableBlocks = (uint)Math.Min(stats.AvailableBytes / blockSize, uint.MaxValue),
            };
        }
        catch (NfsException ex)
        {
            result = Nfs2StatFsResult.Failure(ex.Status);
        }

        return Encode(result);
    }

    private async ValueTask ApplyInitialAttributesAsync(
        NfsFileHandle handle,
        Nfs2SetAttributes attributes,
        CancellationToken cancellationToken)
    {
        NfsSetAttributes changes = Nfs2Mapping.ToSetAttributes(attributes);
        if (changes is { Mode: null, Uid: null, Gid: null, Size: null, AccessTime: null, ModifyTime: null })
        {
            return;
        }

        try
        {
            await _fileSystem.SetAttributesAsync(handle, changes, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException)
        {
            // The object was created; failing to apply initial attributes is non-fatal here.
        }
    }

    private async ValueTask<Nfs2DirOpResult> BuildDirOpAsync(NfsFileHandle handle, CancellationToken cancellationToken)
    {
        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false);
        return Nfs2DirOpResult.Success(Nfs2Mapping.ToWire(handle), Nfs2Mapping.ToWire(attributes));
    }

    private static T Decode<T>(ReadOnlyMemory<byte> arguments)
        where T : IXdrSerializable<T>
    {
        var reader = new XdrReader(arguments.Span);
#if NET7_0_OR_GREATER
        return T.ReadFrom(ref reader);
#else
        return XdrDecoder.ReadFrom<T>(ref reader);
#endif
    }

    private static Nfs2WriteRequest DecodeWrite(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        Nfs2Handle file = Nfs2Handle.ReadFrom(ref reader);
        _ = reader.ReadUInt32();
        uint offset = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        int length = reader.ReadLength(Nfs2.MaxData);
        int dataOffset = reader.Position;
        _ = reader.ReadOpaqueFixed(length);
        return new Nfs2WriteRequest(
            file,
            offset,
            arguments.Slice(dataOffset, length));
    }

    private static RpcReplyPayload Encode<T>(T result)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }

    private static RpcReplyPayload EncodeReadSuccess(Nfs2FileAttributes attributes, ReadOnlySpan<byte> data)
    {
        var buffer = new ArrayBufferWriter<byte>(data.Length + 96);
        var writer = new XdrWriter(buffer);
        writer.WriteInt32((int)NfsStatus.Ok);
        attributes.WriteTo(ref writer);
        writer.WriteOpaqueVariable(data);
        return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
    }

    private readonly record struct Nfs2WriteRequest(
        Nfs2Handle File,
        uint Offset,
        ReadOnlyMemory<byte> Data);
}
