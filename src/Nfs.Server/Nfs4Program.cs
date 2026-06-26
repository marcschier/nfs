using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using Nfs.Abstractions;
using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Server;

/// <summary>
/// An <see cref="IRpcProgram"/> that serves the NFS version 4.0 program (100003, version 4) by
/// processing COMPOUND requests against a pluggable <see cref="INfsFileSystem"/>. This is a
/// stateless subset: it implements the file-handle, attribute, and I/O operations but not the
/// stateful OPEN/CLOSE/lock operations (those return NFS4ERR_NOTSUPP).
/// </summary>
public sealed class Nfs4Program : IRpcProgram, IRpcSecurityAware, IRpcLocalEndPointAware
{
    private const uint OffloadStateIdMagic = 0x4F46464Cu;

    private static readonly byte[] KerberosV5Oid = [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x12, 0x01, 0x02, 0x02];

    private readonly INfsFileSystem _fileSystem;
    private readonly byte[] _writeVerifier = RandomNumberGenerator.GetBytes(Nfs4.VerifierSize);
    private readonly Nfs4StateStore _state;
    private readonly Nfs41SessionStore _sessions;
    private readonly INfs41BackChannelTransport? _backChannelTransport;
    private readonly TimeProvider _timeProvider;
    private readonly Nfs4PnfsOptions _pnfsOptions;
    private readonly object _offloadGate = new();
    private readonly Dictionary<string, OffloadRecord> _offloads = new(StringComparer.Ordinal);
    private readonly object _exclusiveCreateGate = new();
    private readonly Dictionary<string, byte[]> _exclusiveCreateVerifiers = new(StringComparer.Ordinal);
    private IPEndPoint? _copySourceEndPoint;
    private ulong _nextOffloadId;
    private bool _rpcSecGssEnabled;

    /// <summary>Creates a handler backed by <paramref name="fileSystem"/>.</summary>
    /// <param name="fileSystem">The storage backend to serve.</param>
    /// <param name="timeProvider">The clock used for leases and reboot grace.</param>
    /// <param name="delegationRecallTimeout">The time to wait for DELEGRETURN before revoking a recalled delegation.</param>
    /// <param name="rpcSecGssEnabled">Whether SECINFO should advertise RPCSEC_GSS flavors.</param>
    /// <param name="stableStorage">The stable storage used for NFSv4 client recovery records.</param>
    /// <param name="backChannelTransport">The optional NFSv4.1 session back-channel transport.</param>
    /// <param name="pnfsOptions">The optional pNFS files-layout device configuration.</param>
    public Nfs4Program(
        INfsFileSystem fileSystem,
        TimeProvider? timeProvider = null,
        TimeSpan? delegationRecallTimeout = null,
        bool rpcSecGssEnabled = false,
        IStableStorage? stableStorage = null,
        INfs41BackChannelTransport? backChannelTransport = null,
        Nfs4PnfsOptions? pnfsOptions = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _state = new Nfs4StateStore(timeProvider, delegationRecallTimeout: delegationRecallTimeout, stableStorage: stableStorage);
        _sessions = new Nfs41SessionStore(timeProvider, stableStorage);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rpcSecGssEnabled = rpcSecGssEnabled;
        _backChannelTransport = backChannelTransport;
        _pnfsOptions = pnfsOptions ?? new Nfs4PnfsOptions(["127.0.0.1.0.0"], Nfs4Pnfs.DefaultStripeUnit);
    }

    private Nfs4StatusResult BackchannelCtl(Nfs4BackchannelCtlOp op, CompoundContext context) =>
        new(Nfs4Op.BackchannelCtl)
        {
            Status = _sessions.UpdateBackChannel(context.SessionId, op.CallbackProgram, op.CallbackSecurityFlavors)
                ? Nfs4Status.Ok
                : Nfs4Status.BadSession,
        };

    private Nfs4BindConnToSessionResult BindConnToSession(Nfs4BindConnToSessionOp op, CompoundContext context)
    {
        (Nfs4Status status, Nfs4ChannelDirectionFromServer direction, bool useRdma) = _sessions.BindConnection(
            op.SessionId,
            op.Direction,
            op.UseConnectionInRdmaMode,
            context.Connection);
        return new Nfs4BindConnToSessionResult
        {
            Status = status,
            SessionId = op.SessionId,
            Direction = direction,
            UseConnectionInRdmaMode = useRdma,
        };
    }

    private Nfs4StatusResult FreeStateId(Nfs4FreeStateIdOp op) => new(Nfs4Op.FreeStateId)
    {
        Status = _state.FreeStateId(op.StateId),
    };

    private Nfs4TestStateIdResult TestStateId(Nfs4TestStateIdOp op)
    {
        var result = new Nfs4TestStateIdResult { Status = Nfs4Status.Ok };
        foreach (Nfs4StateId stateId in op.StateIds)
        {
            result.StateStatuses.Add(_state.CheckStateId(stateId));
        }

        return result;
    }

    /// <inheritdoc/>
    public uint Program => Nfs4.Program;

    /// <inheritdoc/>
    public void SetRpcSecGssEnabled(bool enabled) => _rpcSecGssEnabled = enabled;

    /// <inheritdoc/>
    public void SetRpcLocalEndPoint(IPEndPoint endPoint) => _copySourceEndPoint = endPoint;

    /// <inheritdoc/>
    public async ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        if (request.Version != Nfs4.ProtocolVersion)
        {
            return RpcReplyPayload.ProgramMismatch(Nfs4.ProtocolVersion, Nfs4.ProtocolVersion);
        }

        return (Nfs4Procedure)request.Procedure switch
        {
            Nfs4Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nfs4Procedure.Compound => await CompoundAsync(request, arguments, cancellationToken).ConfigureAwait(false),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };
    }

    private async ValueTask<RpcReplyPayload> CompoundAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        Nfs4CompoundArgs args = DecodeCompound(arguments);
        _state.ExpireLeases();
        var result = new Nfs4CompoundResult { Tag = args.Tag };

        if (args.MinorVersion is not (Nfs4.MinorVersion0 or Nfs4.MinorVersion1 or Nfs4.MinorVersion2))
        {
            result.Status = Nfs4Status.MinorVersionMismatch;
            return EncodeCompound(result);
        }

        // Version 4.1 and 4.2 COMPOUNDs that begin with SEQUENCE are sequenced through the session
        // reply cache; everything else (including the session-establishing operations) runs directly.
        if (args.MinorVersion is Nfs4.MinorVersion1 or Nfs4.MinorVersion2 &&
            args.Operations is [Nfs4SequenceOp sequence, ..])
        {
            return await SequencedCompoundAsync(args, sequence, request, cancellationToken).ConfigureAwait(false);
        }

        var context = CreateContext(request);
        Nfs4Status status = Nfs4Status.Ok;
        foreach (Nfs4ArgOp operation in args.Operations)
        {
            Nfs4ResOp resop = await ExecuteAsync(operation, context, cancellationToken).ConfigureAwait(false);
            result.Operations.Add(resop);
            status = resop.Status;
            if (status != Nfs4Status.Ok)
            {
                break;
            }
        }

        result.Status = status;
        return EncodeCompound(result);
    }

    private async ValueTask<RpcReplyPayload> SequencedCompoundAsync(
        Nfs4CompoundArgs args,
        Nfs4SequenceOp sequence,
        RpcCallInfo request,
        CancellationToken cancellationToken)
    {
        (Nfs41SessionStore.SequenceKind kind, byte[]? cached, Nfs4Status status, Nfs4SequenceResult? sequenceResult) =
            _sessions.BeginSequence(sequence);

        if (kind == Nfs41SessionStore.SequenceKind.Cached)
        {
            return RpcReplyPayload.Success(cached!);
        }

        var result = new Nfs4CompoundResult { Tag = args.Tag };
        if (kind == Nfs41SessionStore.SequenceKind.Error)
        {
            result.Operations.Add(new Nfs4SequenceResult { Status = status });
            result.Status = status;
            return EncodeCompound(result);
        }

        result.Operations.Add(sequenceResult!);
        Nfs4Status overall = Nfs4Status.Ok;
        var context = CreateContext(request);
        context.SessionClientId = _sessions.GetClientId(sequence.SessionId);
        context.SessionId = sequence.SessionId;
        foreach (Nfs4ArgOp operation in args.Operations.Skip(1))
        {
            Nfs4ResOp resop = await ExecuteAsync(operation, context, cancellationToken).ConfigureAwait(false);
            result.Operations.Add(resop);
            overall = resop.Status;
            if (overall != Nfs4Status.Ok)
            {
                break;
            }
        }

        result.Status = overall;
        byte[] bytes = EncodeBytes(result);
        _sessions.CacheReply(sequence, bytes);
        return RpcReplyPayload.Success(bytes);
    }

    private async ValueTask<Nfs4ResOp> ExecuteAsync(
        Nfs4ArgOp operation,
        CompoundContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return operation switch
            {
                Nfs4PutRootFhOp => await PutRootFhAsync(context, cancellationToken).ConfigureAwait(false),
                Nfs4PutFhOp putFh => PutFh(context, putFh),
                Nfs4GetFhOp => GetFh(context),
                Nfs4SaveFhOp => SaveFh(context),
                Nfs4RestoreFhOp => RestoreFh(context),
                Nfs4LookupOp lookup => await LookupAsync(context, lookup, cancellationToken).ConfigureAwait(false),
                Nfs4LookupParentOp => await LookupParentAsync(context, cancellationToken).ConfigureAwait(false),
                Nfs4SecInfoOp secInfo => await SecInfoAsync(context, secInfo, cancellationToken).ConfigureAwait(false),
                Nfs4SecInfoNoNameOp secInfoNoName => await SecInfoNoNameAsync(
                    context, secInfoNoName, cancellationToken).ConfigureAwait(false),
                Nfs4GetAttrOp getAttr => await GetAttrAsync(context, getAttr, cancellationToken).ConfigureAwait(false),
                Nfs4VerifyOp verify => await VerifyAsync(context, verify, cancellationToken).ConfigureAwait(false),
                Nfs4NverifyOp nverify => await NverifyAsync(context, nverify, cancellationToken).ConfigureAwait(false),
                Nfs4AccessOp access => await AccessAsync(context, access, cancellationToken).ConfigureAwait(false),
                Nfs4ReadOp read => await ReadAsync(context, read, cancellationToken).ConfigureAwait(false),
                Nfs4WriteOp write => await WriteAsync(context, write, cancellationToken).ConfigureAwait(false),
                Nfs4ReadLinkOp => await ReadLinkAsync(context, cancellationToken).ConfigureAwait(false),
                Nfs4ReadDirOp readDir => await ReadDirAsync(context, readDir, cancellationToken).ConfigureAwait(false),
                Nfs4RemoveOp remove => await RemoveAsync(context, remove, cancellationToken).ConfigureAwait(false),
                Nfs4RenameOp rename => await RenameAsync(context, rename, cancellationToken).ConfigureAwait(false),
                Nfs4CreateOp create => await CreateAsync(context, create, cancellationToken).ConfigureAwait(false),
                Nfs4SetAttrOp setAttr => await SetAttrAsync(context, setAttr, cancellationToken).ConfigureAwait(false),
                Nfs4SetClientIdOp setClientId => SetClientId(setClientId),
                Nfs4SetClientIdConfirmOp confirm => SetClientIdConfirm(confirm),
                Nfs4OpenOp open => await OpenAsync(context, open, cancellationToken).ConfigureAwait(false),
                Nfs4OpenConfirmOp openConfirm => OpenConfirm(openConfirm),
                Nfs4OpenDowngradeOp openDowngrade => OpenDowngrade(openDowngrade),
                Nfs4CloseOp close => Close(close),
                Nfs4RenewOp renew => Renew(renew),
                Nfs4DelegReturnOp delegReturn => DelegReturn(delegReturn),
                Nfs4LockOp lockOp => await LockAsync(context, lockOp, cancellationToken).ConfigureAwait(false),
                Nfs4LockTestOp lockTest => LockTest(context, lockTest),
                Nfs4LockUnlockOp lockUnlock => await LockUnlockAsync(lockUnlock, cancellationToken).ConfigureAwait(false),
                Nfs4BackchannelCtlOp backchannelCtl => BackchannelCtl(backchannelCtl, context),
                Nfs4BindConnToSessionOp bindConnToSession => BindConnToSession(bindConnToSession, context),
                Nfs4ExchangeIdOp exchangeId => ExchangeId(exchangeId),
                Nfs4CreateSessionOp createSession => CreateSession(createSession, context),
                Nfs4DestroySessionOp destroySession => DestroySession(destroySession),
                Nfs4FreeStateIdOp freeStateId => FreeStateId(freeStateId),
                Nfs4TestStateIdOp testStateId => TestStateId(testStateId),
                Nfs4DestroyClientIdOp destroyClientId => DestroyClientId(destroyClientId),
                Nfs4ReclaimCompleteOp => ReclaimComplete(),
                Nfs4GetDeviceInfoOp getDeviceInfo => GetDeviceInfo(getDeviceInfo),
                Nfs4LayoutGetOp layoutGet => LayoutGet(context, layoutGet),
                Nfs4LayoutCommitOp layoutCommit => await LayoutCommitAsync(
                    context,
                    layoutCommit,
                    cancellationToken).ConfigureAwait(false),
                Nfs4LayoutReturnOp layoutReturn => LayoutReturn(layoutReturn),
                Nfs4CopyOp copy => await CopyAsync(context, copy, cancellationToken).ConfigureAwait(false),
                Nfs4CopyNotifyOp copyNotify => CopyNotify(context, copyNotify),
                Nfs4OffloadCancelOp offloadCancel => OffloadCancel(offloadCancel),
                Nfs4OffloadStatusOp offloadStatus => OffloadStatus(offloadStatus),
                Nfs4AllocateOp allocate => await AllocateAsync(context, allocate, cancellationToken).ConfigureAwait(false),
                Nfs4DeallocateOp deallocate => await DeallocateAsync(context, deallocate, cancellationToken).ConfigureAwait(false),
                Nfs4ReadPlusOp readPlus => await ReadPlusAsync(context, readPlus, cancellationToken).ConfigureAwait(false),
                Nfs4SeekOp seek => await SeekAsync(context, seek, cancellationToken).ConfigureAwait(false),
                Nfs4CloneOp clone => await CloneAsync(context, clone, cancellationToken).ConfigureAwait(false),
                Nfs4GetXattrOp getXattr => await GetXattrAsync(context, getXattr, cancellationToken).ConfigureAwait(false),
                Nfs4SetXattrOp setXattr => await SetXattrAsync(context, setXattr, cancellationToken).ConfigureAwait(false),
                Nfs4ListXattrsOp listXattrs => await ListXattrsAsync(context, listXattrs, cancellationToken).ConfigureAwait(false),
                Nfs4RemoveXattrOp removeXattr => await RemoveXattrAsync(context, removeXattr, cancellationToken).ConfigureAwait(false),
                _ => new Nfs4StatusResult(operation.Op) { Status = Nfs4Status.NotSupported },
            };
        }
        catch (NfsException ex)
        {
            return Failed(operation.Op, Nfs4StatusMapping.FromStatus(ex.Status));
        }
    }

    private async ValueTask<Nfs4ResOp> PutRootFhAsync(CompoundContext context, CancellationToken cancellationToken)
    {
        context.Current = await _fileSystem.GetRootHandleAsync(cancellationToken).ConfigureAwait(false);
        return new Nfs4StatusResult(Nfs4Op.PutRootFh) { Status = Nfs4Status.Ok };
    }

    private static Nfs4StatusResult PutFh(CompoundContext context, Nfs4PutFhOp op)
    {
        context.Current = Nfs4Mapping.ToHandle(op.Handle);
        return new Nfs4StatusResult(Nfs4Op.PutFh) { Status = Nfs4Status.Ok };
    }

    private static Nfs4ResOp GetFh(CompoundContext context)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.GetFh) { Status = Nfs4Status.NoFileHandle };
        }

        return new Nfs4GetFhResult { Status = Nfs4Status.Ok, Handle = Nfs4Mapping.ToWire(current) };
    }

    private static Nfs4StatusResult SaveFh(CompoundContext context)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.SaveFh) { Status = Nfs4Status.NoFileHandle };
        }

        context.Saved = current;
        return new Nfs4StatusResult(Nfs4Op.SaveFh) { Status = Nfs4Status.Ok };
    }

    private static Nfs4StatusResult RestoreFh(CompoundContext context)
    {
        if (context.Saved is not { } saved)
        {
            return new Nfs4StatusResult(Nfs4Op.RestoreFh) { Status = Nfs4Status.NoFileHandle };
        }

        context.Current = saved;
        return new Nfs4StatusResult(Nfs4Op.RestoreFh) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4ResOp> LookupAsync(
        CompoundContext context,
        Nfs4LookupOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } directory)
        {
            return new Nfs4StatusResult(Nfs4Op.Lookup) { Status = Nfs4Status.NoFileHandle };
        }

        context.Current = await _fileSystem.LookupAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        return new Nfs4StatusResult(Nfs4Op.Lookup) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4ResOp> LookupParentAsync(
        CompoundContext context,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.LookupParent) { Status = Nfs4Status.NoFileHandle };
        }

        context.Current = await _fileSystem.LookupParentAsync(current, cancellationToken).ConfigureAwait(false);
        return new Nfs4StatusResult(Nfs4Op.LookupParent) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4ResOp> SecInfoAsync(
        CompoundContext context,
        Nfs4SecInfoOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } directory)
        {
            return new Nfs4SecInfoResult(Nfs4Op.SecInfo) { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileHandle target = await _fileSystem.LookupAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        _ = await _fileSystem.GetAttributesAsync(target, cancellationToken).ConfigureAwait(false);
        return new Nfs4SecInfoResult(Nfs4Op.SecInfo)
        {
            Status = Nfs4Status.Ok,
            Flavors = BuildSecurityFlavors(context.AdvertiseRpcSecGss),
        };
    }

    private async ValueTask<Nfs4ResOp> SecInfoNoNameAsync(
        CompoundContext context,
        Nfs4SecInfoNoNameOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4SecInfoResult(Nfs4Op.SecInfoNoName) { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileHandle target = op.Style switch
        {
            Nfs4SecInfoStyle.CurrentFileHandle => current,
            Nfs4SecInfoStyle.Parent => await _fileSystem.LookupParentAsync(current, cancellationToken).ConfigureAwait(false),
            _ => throw new NfsException(NfsStatus.InvalidArgument),
        };

        _ = await _fileSystem.GetAttributesAsync(target, cancellationToken).ConfigureAwait(false);
        return new Nfs4SecInfoResult(Nfs4Op.SecInfoNoName)
        {
            Status = Nfs4Status.Ok,
            Flavors = BuildSecurityFlavors(context.AdvertiseRpcSecGss),
        };
    }

    private async ValueTask<Nfs4ResOp> GetAttrAsync(
        CompoundContext context,
        Nfs4GetAttrOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.GetAttr) { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(current, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NfsAccessControlEntry>? acl = op.Request.IsSet(Nfs4AttributeId.Acl)
            ? await _fileSystem.GetAccessControlListAsync(current, cancellationToken).ConfigureAwait(false)
            : null;
        Nfs4FAttr encoded = Nfs4Mapping.BuildAttributes(attributes, current, acl).Encode(op.Request);
        return new Nfs4GetAttrResult { Status = Nfs4Status.Ok, Attributes = encoded };
    }

    private async ValueTask<Nfs4StatusResult> VerifyAsync(
        CompoundContext context,
        Nfs4VerifyOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.Verify) { Status = Nfs4Status.NoFileHandle };
        }

        Nfs4FAttr attributes = await EncodeCurrentAttributesAsync(
            current, op.Attributes.Mask, cancellationToken).ConfigureAwait(false);
        bool same = AreSameAttributes(op.Attributes, attributes);
        return new Nfs4StatusResult(Nfs4Op.Verify)
        {
            Status = same ? Nfs4Status.Ok : Nfs4Status.NotSame,
        };
    }

    private async ValueTask<Nfs4StatusResult> NverifyAsync(
        CompoundContext context,
        Nfs4NverifyOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.NVerify) { Status = Nfs4Status.NoFileHandle };
        }

        Nfs4FAttr attributes = await EncodeCurrentAttributesAsync(
            current, op.Attributes.Mask, cancellationToken).ConfigureAwait(false);
        bool same = AreSameAttributes(op.Attributes, attributes);
        return new Nfs4StatusResult(Nfs4Op.NVerify)
        {
            Status = same ? Nfs4Status.Same : Nfs4Status.Ok,
        };
    }

    private async ValueTask<Nfs4ResOp> AccessAsync(
        CompoundContext context,
        Nfs4AccessOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.Access) { Status = Nfs4Status.NoFileHandle };
        }

        // Validate the handle, then grant every requested right (this backend does not enforce ACLs).
        _ = await _fileSystem.GetAttributesAsync(current, cancellationToken).ConfigureAwait(false);
        const uint allKnown = 0x3F; // READ, LOOKUP, MODIFY, EXTEND, DELETE, EXECUTE
        uint supported = op.Access & allKnown;
        return new Nfs4AccessResult { Status = Nfs4Status.Ok, Supported = supported, Access = supported };
    }

    private async ValueTask<Nfs4ResOp> ReadAsync(
        CompoundContext context,
        Nfs4ReadOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.Read) { Status = Nfs4Status.NoFileHandle };
        }

        if (IsOffloadStateId(op.StateId))
        {
            NfsFileHandle? authorizedSource = TryGetCopyNotifySource(op.StateId);
            if (authorizedSource is null || authorizedSource.Value != current)
            {
                return new Nfs4ReadResult { Status = Nfs4Status.BadStateId };
            }
        }

        uint count = Math.Min(op.Count, Nfs4.MaxIoSize);
        ulong clientId = _state.GetStateClientId(op.StateId);
        if (await RecallConflictingDelegationAsync(
                current,
                clientId,
                writeAccess: false,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4ReadResult { Status = Nfs4Status.Delay };
        }

        NfsReadResult read = await _fileSystem
            .ReadAsync(current, op.Offset, count, cancellationToken).ConfigureAwait(false);
        return new Nfs4ReadResult
        {
            Status = Nfs4Status.Ok,
            Eof = read.EndOfFile,
            Data = MemoryHelpers.ToArrayOrExactArray(read.Data),
        };
    }

    private async ValueTask<Nfs4ResOp> WriteAsync(
        CompoundContext context,
        Nfs4WriteOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.Write) { Status = Nfs4Status.NoFileHandle };
        }

        if (!IsAnonymousStateId(op.StateId) && !_state.HasWriteOpenAccess(op.StateId))
        {
            return new Nfs4WriteResult { Status = Nfs4Status.BadStateId };
        }

        ulong clientId = _state.GetOpenClientId(op.StateId);
        if (await RecallConflictingDelegationAsync(
                current,
                clientId,
                writeAccess: true,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4WriteResult { Status = Nfs4Status.Delay };
        }

        NfsWriteResult write = await _fileSystem
            .WriteAsync(current, op.Offset, op.Data, cancellationToken).ConfigureAwait(false);
        return new Nfs4WriteResult
        {
            Status = Nfs4Status.Ok,
            Count = write.Count,
            Committed = 2, // FILE_SYNC4
            Verifier = _writeVerifier,
        };
    }

    private async ValueTask<Nfs4ResOp> ReadLinkAsync(CompoundContext context, CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.ReadLink) { Status = Nfs4Status.NoFileHandle };
        }

        string target = await _fileSystem.ReadSymbolicLinkAsync(current, cancellationToken).ConfigureAwait(false);
        return new Nfs4ReadLinkResult { Status = Nfs4Status.Ok, Target = target };
    }

    private async ValueTask<Nfs4ResOp> ReadDirAsync(
        CompoundContext context,
        Nfs4ReadDirOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.ReadDir) { Status = Nfs4Status.NoFileHandle };
        }

        NfsDirectoryListing listing = await _fileSystem
            .ReadDirectoryAsync(current, op.Cookie, op.DirectoryCount, cancellationToken).ConfigureAwait(false);

        var entries = new Nfs4DirEntry[listing.Entries.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            NfsDirectoryEntry entry = listing.Entries[i];
            Nfs4FAttr attributes = default;
            try
            {
                NfsFileHandle child = await _fileSystem
                    .LookupAsync(current, entry.Name, cancellationToken).ConfigureAwait(false);
                NfsFileAttributes childAttributes = await _fileSystem
                    .GetAttributesAsync(child, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<NfsAccessControlEntry>? childAcl = op.Request.IsSet(Nfs4AttributeId.Acl)
                    ? await _fileSystem.GetAccessControlListAsync(child, cancellationToken).ConfigureAwait(false)
                    : null;
                attributes = Nfs4Mapping.BuildAttributes(childAttributes, child, childAcl).Encode(op.Request);
            }
            catch (NfsException)
            {
                attributes = new Nfs4FAttr { Mask = Nfs4Bitmap.Empty, Values = [] };
            }

            entries[i] = new Nfs4DirEntry(entry.Cookie, entry.Name, attributes);
        }

        return new Nfs4ReadDirResult
        {
            Status = Nfs4Status.Ok,
            CookieVerifier = new byte[Nfs4.VerifierSize],
            Entries = entries,
            Eof = listing.EndOfStream,
        };
    }

    private async ValueTask<Nfs4ResOp> RemoveAsync(
        CompoundContext context,
        Nfs4RemoveOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } directory)
        {
            return new Nfs4StatusResult(Nfs4Op.Remove) { Status = Nfs4Status.NoFileHandle };
        }

        ulong before = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);
        NfsFileHandle child = await _fileSystem.LookupAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        NfsFileAttributes childAttributes = await _fileSystem
            .GetAttributesAsync(child, cancellationToken).ConfigureAwait(false);
        if (await RecallConflictingDelegationAsync(
                child,
                requesterClientId: 0,
                writeAccess: true,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4RemoveResult { Status = Nfs4Status.Delay };
        }

        if (childAttributes.Type == NfsFileType.Directory)
        {
            await _fileSystem.RemoveDirectoryAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _fileSystem.RemoveAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        }

        ulong after = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);
        return new Nfs4RemoveResult
        {
            Status = Nfs4Status.Ok,
            ChangeInfo = new Nfs4ChangeInfo { Atomic = false, Before = before, After = after },
        };
    }

    private async ValueTask<Nfs4ResOp> RenameAsync(
        CompoundContext context,
        Nfs4RenameOp op,
        CancellationToken cancellationToken)
    {
        if (context.Saved is not { } source || context.Current is not { } target)
        {
            return new Nfs4StatusResult(Nfs4Op.Rename) { Status = Nfs4Status.NoFileHandle };
        }

        ulong sourceBefore = await ChangeOfAsync(source, cancellationToken).ConfigureAwait(false);
        ulong targetBefore = await ChangeOfAsync(target, cancellationToken).ConfigureAwait(false);
        NfsFileHandle child = await _fileSystem.LookupAsync(source, op.OldName, cancellationToken).ConfigureAwait(false);
        if (await RecallConflictingDelegationAsync(
                child,
                requesterClientId: 0,
                writeAccess: true,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4RenameResult { Status = Nfs4Status.Delay };
        }

        await _fileSystem
            .RenameAsync(source, op.OldName, target, op.NewName, cancellationToken).ConfigureAwait(false);
        ulong sourceAfter = await ChangeOfAsync(source, cancellationToken).ConfigureAwait(false);
        ulong targetAfter = await ChangeOfAsync(target, cancellationToken).ConfigureAwait(false);

        return new Nfs4RenameResult
        {
            Status = Nfs4Status.Ok,
            Source = new Nfs4ChangeInfo { Atomic = false, Before = sourceBefore, After = sourceAfter },
            Target = new Nfs4ChangeInfo { Atomic = false, Before = targetBefore, After = targetAfter },
        };
    }

    private async ValueTask<Nfs4ResOp> CreateAsync(
        CompoundContext context,
        Nfs4CreateOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } directory)
        {
            return new Nfs4StatusResult(Nfs4Op.Create) { Status = Nfs4Status.NoFileHandle };
        }

        ulong before = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);
        NfsFileHandle created = op.Type switch
        {
            Nfs4CreateType.Directory => await _fileSystem
                .MakeDirectoryAsync(directory, op.Name, cancellationToken).ConfigureAwait(false),
            Nfs4CreateType.SymbolicLink => await _fileSystem
                .CreateSymbolicLinkAsync(directory, op.Name, op.LinkTarget, cancellationToken).ConfigureAwait(false),
            _ => throw new NfsException(NfsStatus.NotSupported),
        };

        (NfsSetAttributes changes, Nfs4Bitmap applied) = Nfs4Mapping.ToSetAttributes(op.Attributes);
        if (applied != Nfs4Bitmap.Empty)
        {
            try
            {
                await _fileSystem.SetAttributesAsync(created, changes, cancellationToken).ConfigureAwait(false);
            }
            catch (NfsException)
            {
                applied = Nfs4Bitmap.Empty;
            }
        }

        ulong after = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);
        context.Current = created;
        return new Nfs4CreateResult
        {
            Status = Nfs4Status.Ok,
            ChangeInfo = new Nfs4ChangeInfo { Atomic = false, Before = before, After = after },
            AttributesSet = applied,
        };
    }

    private async ValueTask<Nfs4ResOp> SetAttrAsync(
        CompoundContext context,
        Nfs4SetAttrOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4StatusResult(Nfs4Op.SetAttr) { Status = Nfs4Status.NoFileHandle };
        }

        ulong clientId = _state.GetOpenClientId(op.StateId);
        if (await RecallConflictingDelegationAsync(
                current,
                clientId,
                writeAccess: true,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4SetAttrResult { Status = Nfs4Status.Delay };
        }

        (NfsSetAttributes changes, Nfs4Bitmap applied) = Nfs4Mapping.ToSetAttributes(op.Attributes);
        if (changes.AccessControlList is { } acl)
        {
            await _fileSystem.SetAccessControlListAsync(current, acl, cancellationToken).ConfigureAwait(false);
        }

        if (changes.Mode.HasValue || changes.Size.HasValue || changes.AccessTime.HasValue || changes.ModifyTime.HasValue)
        {
            await _fileSystem.SetAttributesAsync(current, changes, cancellationToken).ConfigureAwait(false);
        }

        return new Nfs4SetAttrResult { Status = Nfs4Status.Ok, AttributesSet = applied };
    }

    private async ValueTask<Nfs4ResOp> GetXattrAsync(
        CompoundContext context,
        Nfs4GetXattrOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4GetXattrResult { Status = Nfs4Status.NoFileHandle };
        }

        byte[] value = await _fileSystem.GetExtendedAttributeAsync(current, op.Name, cancellationToken).ConfigureAwait(false);
        return new Nfs4GetXattrResult { Status = Nfs4Status.Ok, Value = value };
    }

    private async ValueTask<Nfs4ResOp> SetXattrAsync(
        CompoundContext context,
        Nfs4SetXattrOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4SetXattrResult { Status = Nfs4Status.NoFileHandle };
        }

        ulong before = await ChangeOfAsync(current, cancellationToken).ConfigureAwait(false);
        await _fileSystem
            .SetExtendedAttributeAsync(
                current,
                op.Name,
                op.Value,
                (NfsSetExtendedAttributeMode)op.Option,
                cancellationToken)
            .ConfigureAwait(false);
        ulong after = await ChangeOfAsync(current, cancellationToken).ConfigureAwait(false);
        return new Nfs4SetXattrResult
        {
            Status = Nfs4Status.Ok,
            ChangeInfo = new Nfs4ChangeInfo { Atomic = false, Before = before, After = after },
        };
    }

    private async ValueTask<Nfs4ResOp> ListXattrsAsync(
        CompoundContext context,
        Nfs4ListXattrsOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4ListXattrsResult { Status = Nfs4Status.NoFileHandle };
        }

        NfsExtendedAttributeListing listing = await _fileSystem
            .ListExtendedAttributesAsync(current, op.Cookie, op.MaxCount, cancellationToken).ConfigureAwait(false);
        return new Nfs4ListXattrsResult
        {
            Status = Nfs4Status.Ok,
            Cookie = listing.Cookie,
            Names = [.. listing.Names],
            Eof = listing.EndOfList,
        };
    }

    private async ValueTask<Nfs4ResOp> RemoveXattrAsync(
        CompoundContext context,
        Nfs4RemoveXattrOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4RemoveXattrResult { Status = Nfs4Status.NoFileHandle };
        }

        ulong before = await ChangeOfAsync(current, cancellationToken).ConfigureAwait(false);
        await _fileSystem.RemoveExtendedAttributeAsync(current, op.Name, cancellationToken).ConfigureAwait(false);
        ulong after = await ChangeOfAsync(current, cancellationToken).ConfigureAwait(false);
        return new Nfs4RemoveXattrResult
        {
            Status = Nfs4Status.Ok,
            ChangeInfo = new Nfs4ChangeInfo { Atomic = false, Before = before, After = after },
        };
    }

    private Nfs4SetClientIdResult SetClientId(Nfs4SetClientIdOp op)
    {
        Nfs4ClientCallbackInfo? callback = op.CallbackProgram == 0
            ? null
            : new Nfs4ClientCallbackInfo(
                op.CallbackProgram,
                op.CallbackNetId,
                op.CallbackAddress,
                op.CallbackIdent);
        (ulong clientId, byte[] confirm) = _state.RegisterClient(op.Verifier, op.Id, callback);
        return new Nfs4SetClientIdResult { Status = Nfs4Status.Ok, ClientId = clientId, ConfirmVerifier = confirm };
    }

    private Nfs4StatusResult SetClientIdConfirm(Nfs4SetClientIdConfirmOp op)
    {
        bool confirmed = _state.ConfirmClient(op.ClientId, op.Confirm);
        return new Nfs4StatusResult(Nfs4Op.SetClientIdConfirm)
        {
            Status = confirmed ? Nfs4Status.Ok : Nfs4Status.StaleClientId,
        };
    }

    private async ValueTask<Nfs4ResOp> OpenAsync(
        CompoundContext context,
        Nfs4OpenOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } directory)
        {
            return new Nfs4OpenResult { Status = Nfs4Status.NoFileHandle };
        }

        if (op.Reclaim)
        {
            Nfs4Status reclaimStatus = _state.CheckReclaim(op.ClientId);
            if (reclaimStatus != Nfs4Status.Ok)
            {
                return new Nfs4OpenResult { Status = reclaimStatus };
            }
        }
        else if (_state.IsInGrace)
        {
            return new Nfs4OpenResult { Status = Nfs4Status.Grace };
        }

        ulong before = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);

        NfsFileHandle file;
        Nfs4Bitmap applied = Nfs4Bitmap.Empty;
        bool created = false;
        if (op.Reclaim)
        {
            file = directory;
        }
        else if (op.OpenType == Nfs4OpenType.Create)
        {
            NfsFileHandle? existing = await TryLookupAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
            if (existing is { } found)
            {
                if (op.CreateMode == Nfs4CreateMode.Exclusive)
                {
                    if (!HasExclusiveCreateVerifier(found, op.CreateVerifier))
                    {
                        return new Nfs4OpenResult { Status = Nfs4Status.AlreadyExists };
                    }
                }
                else if (op.CreateMode == Nfs4CreateMode.Guarded)
                {
                    return new Nfs4OpenResult { Status = Nfs4Status.AlreadyExists };
                }

                file = found;
            }
            else
            {
                file = await _fileSystem.CreateAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
                created = true;
                if (op.CreateMode == Nfs4CreateMode.Exclusive)
                {
                    RememberExclusiveCreateVerifier(file, op.CreateVerifier);
                }
            }

        }
        else
        {
            file = await _fileSystem.LookupAsync(directory, op.Name, cancellationToken).ConfigureAwait(false);
        }

        bool writeShare = IsWriteShare(op.ShareAccess);
        if (await RecallConflictingDelegationAsync(file, op.ClientId, writeShare, cancellationToken)
            .ConfigureAwait(false))
        {
            return new Nfs4OpenResult { Status = Nfs4Status.Delay };
        }

        if (op.OpenType == Nfs4OpenType.Create && op.CreateMode != Nfs4CreateMode.Exclusive)
        {
            applied = await ApplyCreateAttributesAsync(file, op.CreateAttributes, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            (Nfs4StateId stateId, Nfs4StateId? delegationStateId, uint delegationType) =
                _state.Open(file, op.ClientId, op.ShareAccess, op.ShareDeny);
            ulong after = await ChangeOfAsync(directory, cancellationToken).ConfigureAwait(false);
            context.Current = file;
            return new Nfs4OpenResult
            {
                Status = Nfs4Status.Ok,
                StateId = stateId,
                ChangeInfo = new Nfs4ChangeInfo { Atomic = false, Before = before, After = created ? after : before },
                ResultFlags = 0,
                AttributesSet = applied,
                DelegationType = delegationType,
                DelegationStateId = delegationStateId.GetValueOrDefault(),
            };
        }
        catch (NfsException)
        {
            return new Nfs4OpenResult { Status = Nfs4Status.StaleClientId };
        }
    }

    private Nfs4StateIdResult OpenConfirm(Nfs4OpenConfirmOp op)
    {
        try
        {
            Nfs4StateId stateId = _state.ConfirmOpen(op.OpenStateId);
            return new Nfs4StateIdResult(Nfs4Op.OpenConfirm) { Status = Nfs4Status.Ok, StateId = stateId };
        }
        catch (NfsException ex)
        {
            return new Nfs4StateIdResult(Nfs4Op.OpenConfirm) { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
        }
    }

    private Nfs4StateIdResult OpenDowngrade(Nfs4OpenDowngradeOp op)
    {
        try
        {
            Nfs4StateId stateId = _state.DowngradeOpen(op.OpenStateId, op.ShareAccess, op.ShareDeny);
            return new Nfs4StateIdResult(Nfs4Op.OpenDowngrade) { Status = Nfs4Status.Ok, StateId = stateId };
        }
        catch (NfsException ex)
        {
            return new Nfs4StateIdResult(Nfs4Op.OpenDowngrade) { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
        }
    }

    private Nfs4StateIdResult Close(Nfs4CloseOp op)
    {
        try
        {
            Nfs4StateId stateId = _state.Close(op.OpenStateId);
            return new Nfs4StateIdResult(Nfs4Op.Close) { Status = Nfs4Status.Ok, StateId = stateId };
        }
        catch (NfsException ex)
        {
            return new Nfs4StateIdResult(Nfs4Op.Close) { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
        }
    }

    private Nfs4StatusResult Renew(Nfs4RenewOp op) => new(Nfs4Op.Renew)
    {
        Status = _state.Renew(op.ClientId) ? Nfs4Status.Ok : Nfs4Status.StaleClientId,
    };

    private Nfs4StatusResult DelegReturn(Nfs4DelegReturnOp op) => new(Nfs4Op.DelegReturn)
    {
        Status = _state.ReturnDelegation(op.StateId) ? Nfs4Status.Ok : Nfs4Status.BadStateId,
    };

    private async ValueTask<Nfs4LockResult> LockAsync(
        CompoundContext context,
        Nfs4LockOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4LockResult { Status = Nfs4Status.NoFileHandle };
        }

        if (op.Reclaim)
        {
            ulong reclaimClientId = op.NewLockOwner ? op.LockOwner.ClientId : 0;
            Nfs4Status reclaimStatus = _state.CheckReclaim(reclaimClientId);
            if (reclaimStatus != Nfs4Status.Ok)
            {
                return new Nfs4LockResult { Status = reclaimStatus };
            }
        }
        else if (_state.IsInGrace)
        {
            return new Nfs4LockResult { Status = Nfs4Status.Grace };
        }

        bool write = op.LockType is Nfs4LockType.Write or Nfs4LockType.WriteBlocking;
        ulong clientId = op.NewLockOwner ? op.LockOwner.ClientId : _state.GetStateClientId(op.LockStateId);
        if (await RecallConflictingDelegationAsync(file, clientId, write, cancellationToken).ConfigureAwait(false))
        {
            return new Nfs4LockResult { Status = Nfs4Status.Delay };
        }

        Nfs4LockOwner owner = op.NewLockOwner ? op.LockOwner : default;
        Nfs4StateId? existing = op.NewLockOwner ? null : op.LockStateId;

        try
        {
            (Nfs4StateId stateId, Nfs4LockDenied? denied) = _state.Lock(
                file, owner, write, op.Offset, op.Length, existing);
            return denied is { } conflict
                ? new Nfs4LockResult { Status = Nfs4Status.LockDenied, Denied = conflict }
                : new Nfs4LockResult { Status = Nfs4Status.Ok, StateId = stateId };
        }
        catch (NfsException ex)
        {
            return new Nfs4LockResult { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
        }
    }

    private Nfs4LockTestResult LockTest(CompoundContext context, Nfs4LockTestOp op)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4LockTestResult { Status = Nfs4Status.NoFileHandle };
        }

        bool write = op.LockType is Nfs4LockType.Write or Nfs4LockType.WriteBlocking;
        Nfs4LockDenied? denied = _state.TestLock(file, op.Owner, write, op.Offset, op.Length);
        return denied is { } conflict
            ? new Nfs4LockTestResult { Status = Nfs4Status.LockDenied, Denied = conflict }
            : new Nfs4LockTestResult { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4LockUnlockResult> LockUnlockAsync(
        Nfs4LockUnlockOp op,
        CancellationToken cancellationToken)
    {
        try
        {
            (Nfs4StateId stateId, IReadOnlyList<Nfs4LockNotification> notifications) =
                _state.Unlock(op.LockStateId, op.Offset, op.Length);
            await NotifyLockWaitersAsync(notifications, cancellationToken).ConfigureAwait(false);
            return new Nfs4LockUnlockResult { Status = Nfs4Status.Ok, StateId = stateId };
        }
        catch (NfsException ex)
        {
            return new Nfs4LockUnlockResult { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
        }
    }

    private async ValueTask<bool> RecallConflictingDelegationAsync(
        NfsFileHandle file,
        ulong requesterClientId,
        bool writeAccess,
        CancellationToken cancellationToken)
    {
        Nfs4DelegationRecall? recall = _state.PrepareDelegationRecall(
            file,
            requesterClientId,
            writeAccess,
            _sessions.HasBackChannel);
        if (recall is not { } value)
        {
            return false;
        }

        if (value.Notify && value.Callback is { } callback && TryCreateCallbackEndPoint(callback, out IPEndPoint endPoint))
        {
            try
            {
                Nfs4Status status = await Nfs4CallbackClient.RecallAsync(
                    endPoint,
                    callback.Program,
                    callback.Ident,
                    value.StateId,
                    Nfs4Mapping.ToWire(value.File),
                    cancellationToken).ConfigureAwait(false);
                if (status != Nfs4Status.Ok)
                {
                    _state.RevokeDelegation(value.StateId);
                    return false;
                }
            }
            catch (Exception ex) when (ex is RpcException or IOException or SocketException)
            {
                _state.RevokeDelegation(value.StateId);
                return false;
            }
        }
        else if (value.Notify && value.Callback is null)
        {
            if (!await RecallSessionDelegationAsync(value, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }
        else if (value.Notify)
        {
            _state.RevokeDelegation(value.StateId);
            return false;
        }

        return true;
    }

    private async ValueTask NotifyLockWaitersAsync(
        IReadOnlyList<Nfs4LockNotification> notifications,
        CancellationToken cancellationToken)
    {
        foreach (Nfs4LockNotification notification in notifications)
        {
            if (_sessions.NextBackChannelCall(notification.ClientId) is not { } backChannel)
            {
                continue;
            }

            try
            {
                _ = await Nfs4CallbackClient.NotifyLockSessionAsync(
                    backChannel.Transport,
                    backChannel.Call,
                    Nfs4Mapping.ToWire(notification.File),
                    notification.Owner,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RpcException or IOException or SocketException)
            {
            }
        }
    }

    private async ValueTask<bool> RecallSessionDelegationAsync(
        Nfs4DelegationRecall recall,
        CancellationToken cancellationToken)
    {
        if (_sessions.NextBackChannelCall(recall.ClientId) is not { } backChannel)
        {
            _state.RevokeDelegation(recall.StateId);
            return false;
        }

        try
        {
            Nfs4Status status = await Nfs4CallbackClient.RecallSessionAsync(
                backChannel.Transport,
                backChannel.Call,
                recall.StateId,
                Nfs4Mapping.ToWire(recall.File),
                cancellationToken).ConfigureAwait(false);
            if (status == Nfs4Status.Ok)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is RpcException or IOException or SocketException)
        {
        }

        _state.RevokeDelegation(recall.StateId);
        return false;
    }

    private static bool TryCreateCallbackEndPoint(
        Nfs4ClientCallbackInfo callback,
        out IPEndPoint endPoint)
    {
        if (!string.Equals(callback.NetId, "tcp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(callback.NetId, "tcp4", StringComparison.OrdinalIgnoreCase))
        {
            endPoint = null!;
            return false;
        }

        try
        {
            endPoint = Nfs4CallbackClient.ParseTcpUniversalAddress(callback.Address);
            return true;
        }
        catch (FormatException)
        {
            endPoint = null!;
            return false;
        }
    }

    private static bool IsWriteShare(uint shareAccess) =>
        (shareAccess & Nfs4ShareAccess.Write) != 0;

    private Nfs4ExchangeIdResult ExchangeId(Nfs4ExchangeIdOp op)
    {
        (ulong clientId, uint sequenceId) = _sessions.ExchangeId(op.OwnerId);
        return new Nfs4ExchangeIdResult
        {
            Status = Nfs4Status.Ok,
            ClientId = clientId,
            SequenceId = sequenceId,
            Flags = 0,
        };
    }

    private async ValueTask<Nfs4CopyResult> CopyAsync(
        CompoundContext context,
        Nfs4CopyOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } destination)
        {
            return new Nfs4CopyResult { Status = Nfs4Status.NoFileHandle };
        }

        RemoteCopySource? remoteSource = null;
        NfsFileHandle? localSource = null;
        if (op.SourceServers.Count == 0)
        {
            localSource = context.Saved ?? TryGetCopyNotifySource(op.SourceStateId);
            if (localSource is null)
            {
                return new Nfs4CopyResult { Status = Nfs4Status.NoFileHandle };
            }
        }
        else if (TryGetRemoteCopySource(op.SourceServers, out RemoteCopySource parsedRemoteSource))
        {
            remoteSource = parsedRemoteSource;
        }
        else
        {
            return new Nfs4CopyResult { Status = Nfs4Status.InvalidArgument };
        }

        if (!op.Synchronous)
        {
            return StartAsyncCopy(context, op, localSource, destination, remoteSource);
        }

        ulong copied;
        if (remoteSource is null)
        {
            copied = await CopyRangeAsync(
                localSource!.Value,
                destination,
                op.SourceOffset,
                op.DestinationOffset,
                op.Count,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                copied = await CopyRemoteRangeAsync(
                    remoteSource.Value,
                    op.SourceStateId,
                    destination,
                    op.SourceOffset,
                    op.DestinationOffset,
                    op.Count,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new Nfs4CopyResult { Status = Nfs4Status.Delay };
            }
            catch (NfsException ex)
            {
                return new Nfs4CopyResult { Status = Nfs4StatusMapping.FromStatus(ex.Status) };
            }
            catch (Exception ex) when (ex is RpcException or IOException or SocketException)
            {
                return new Nfs4CopyResult { Status = Nfs4Status.IoError };
            }
        }

        return new Nfs4CopyResult
        {
            Status = Nfs4Status.Ok,
            Consecutive = op.Consecutive,
            Synchronous = op.Synchronous,
            Response = new Nfs4CopyWriteResponse
            {
                Count = copied,
                Committed = 2, // FILE_SYNC4
                Verifier = _writeVerifier,
            },
        };
    }

    private Nfs4CopyNotifyResult CopyNotify(CompoundContext context, Nfs4CopyNotifyOp op)
    {
        if (context.Current is not { } source)
        {
            return new Nfs4CopyNotifyResult { Status = Nfs4Status.NoFileHandle };
        }

        Nfs4StateId stateId = NewOffloadStateId();
        var record = new OffloadRecord(stateId, source, default, 0, _timeProvider.GetTimestamp(), null, null);
        StoreOffload(record);
        var result = new Nfs4CopyNotifyResult
        {
            Status = Nfs4Status.Ok,
            LeaseTime = new Nfs4Time { Seconds = 90 },
            StateId = stateId,
        };
        result.SourceLocations.Add(CreateCopyNotifyLocation(source));
        return result;
    }

    private Nfs4CopyResult StartAsyncCopy(
        CompoundContext context,
        Nfs4CopyOp op,
        NfsFileHandle? source,
        NfsFileHandle destination,
        RemoteCopySource? remoteSource)
    {
        Nfs4StateId stateId = NewOffloadStateId();
        var cancellation = new CancellationTokenSource();
        var record = new OffloadRecord(
            stateId,
            source ?? default,
            destination,
            op.Count,
            _timeProvider.GetTimestamp(),
            cancellation,
            remoteSource)
        {
            ClientId = context.SessionClientId,
            SourceStateId = op.SourceStateId,
        };
        StoreOffload(record);
        _ = Task.Run(() => CompleteAsyncCopyAsync(record, op.SourceOffset, op.DestinationOffset));

        return new Nfs4CopyResult
        {
            Status = Nfs4Status.Ok,
            Consecutive = op.Consecutive,
            Synchronous = false,
            Response = new Nfs4CopyWriteResponse
            {
                CallbackId = stateId,
                Count = 0,
                Committed = 2,
                Verifier = _writeVerifier,
            },
        };
    }

    private Nfs4StatusResult OffloadCancel(Nfs4OffloadCancelOp op)
    {
        OffloadRecord? record = FindOffload(op.StateId);
        if (record is null)
        {
            return new Nfs4StatusResult(Nfs4Op.OffloadCancel) { Status = Nfs4Status.BadStateId };
        }

        record.Cancel();
        return new Nfs4StatusResult(Nfs4Op.OffloadCancel) { Status = Nfs4Status.Ok };
    }

    private Nfs4OffloadStatusResult OffloadStatus(Nfs4OffloadStatusOp op)
    {
        OffloadRecord? record = FindOffload(op.StateId);
        if (record is null)
        {
            return new Nfs4OffloadStatusResult { Status = Nfs4Status.BadStateId };
        }

        return new Nfs4OffloadStatusResult
        {
            Status = Nfs4Status.Ok,
            Count = record.Copied,
            Complete = record.Complete,
            CompleteStatus = record.Status,
        };
    }

    private async ValueTask<Nfs4ResOp> AllocateAsync(
        CompoundContext context,
        Nfs4AllocateOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4StatusResult(Nfs4Op.Allocate) { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(file, cancellationToken).ConfigureAwait(false);
        ulong required = op.Offset + op.Length;
        if (required > attributes.Size)
        {
            await _fileSystem
                .SetAttributesAsync(file, new NfsSetAttributes { Size = required }, cancellationToken)
                .ConfigureAwait(false);
        }

        return new Nfs4StatusResult(Nfs4Op.Allocate) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4ResOp> DeallocateAsync(
        CompoundContext context,
        Nfs4DeallocateOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4StatusResult(Nfs4Op.Deallocate) { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(file, cancellationToken).ConfigureAwait(false);
        if (op.Offset < attributes.Size)
        {
            // No sparse-file support: emulate the hole by zeroing the affected bytes.
            ulong end = Math.Min(op.Offset + op.Length, attributes.Size);
            byte[] zeros = new byte[(int)Math.Min(end - op.Offset, Nfs4.MaxIoSize)];
            for (ulong written = op.Offset; written < end;)
            {
                int count = (int)Math.Min((ulong)zeros.Length, end - written);
                await _fileSystem
                    .WriteAsync(file, written, zeros.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                written += (ulong)count;
            }
        }

        return new Nfs4StatusResult(Nfs4Op.Deallocate) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<Nfs4ReadPlusResult> ReadPlusAsync(
        CompoundContext context,
        Nfs4ReadPlusOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } current)
        {
            return new Nfs4ReadPlusResult { Status = Nfs4Status.NoFileHandle };
        }

        uint count = Math.Min(op.Count, Nfs4.MaxIoSize);
        NfsReadResult read = await _fileSystem
            .ReadAsync(current, op.Offset, count, cancellationToken).ConfigureAwait(false);
        return new Nfs4ReadPlusResult
        {
            Status = Nfs4Status.Ok,
            Eof = read.EndOfFile,
            Contents =
            [
                new Nfs4ReadPlusData { Offset = op.Offset, Data = MemoryHelpers.ToArrayOrExactArray(read.Data) },
            ],
        };
    }

    private async ValueTask<Nfs4ResOp> SeekAsync(
        CompoundContext context,
        Nfs4SeekOp op,
        CancellationToken cancellationToken)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4SeekResult { Status = Nfs4Status.NoFileHandle };
        }

        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(file, cancellationToken).ConfigureAwait(false);

        // This backend has no sparse-file metadata, so the file is treated as wholly data with a
        // single implicit hole at end of file.
        if (op.What == Nfs4ContentType.Data)
        {
            return op.Offset < attributes.Size
                ? new Nfs4SeekResult { Status = Nfs4Status.Ok, Eof = false, Offset = op.Offset }
                : new Nfs4SeekResult { Status = Nfs4Status.NoSuchDeviceOrAddress };
        }

        return new Nfs4SeekResult
        {
            Status = Nfs4Status.Ok,
            Eof = op.Offset >= attributes.Size,
            Offset = attributes.Size,
        };
    }

    private async ValueTask<Nfs4StatusResult> CloneAsync(
        CompoundContext context,
        Nfs4CloneOp op,
        CancellationToken cancellationToken)
    {
        if (context.Saved is not { } source || context.Current is not { } destination)
        {
            return new Nfs4StatusResult(Nfs4Op.Clone) { Status = Nfs4Status.NoFileHandle };
        }

        _ = await CopyRangeAsync(
            source,
            destination,
            op.SourceOffset,
            op.DestinationOffset,
            op.Count,
            cancellationToken).ConfigureAwait(false);
        return new Nfs4StatusResult(Nfs4Op.Clone) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<ulong> CopyRangeAsync(
        NfsFileHandle source,
        NfsFileHandle destination,
        ulong sourceOffset,
        ulong destinationOffset,
        ulong count,
        CancellationToken cancellationToken)
    {
        ulong copied = 0;
        using var buffer = new PooledBufferWriter((int)Math.Min((ulong)Nfs4.MaxIoSize, count));
        while (copied < count)
        {
            uint chunk = (uint)Math.Min((ulong)Nfs4.MaxIoSize, count - copied);
            NfsBufferedReadResult read = await _fileSystem
                .ReadAsync(source, sourceOffset + copied, chunk, buffer, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> data = buffer.WrittenMemory;
            if (data.IsEmpty)
            {
                break;
            }

            NfsWriteResult write = await _fileSystem
                .WriteAsync(destination, destinationOffset + copied, data, cancellationToken).ConfigureAwait(false);
            copied += write.Count;
            if (read.EndOfFile || write.Count < read.Count)
            {
                break;
            }

            buffer.Clear();
        }

        return copied;
    }

    private async Task CompleteAsyncCopyAsync(
        OffloadRecord record,
        ulong sourceOffset,
        ulong destinationOffset)
    {
        Nfs4Status status = Nfs4Status.Ok;
        try
        {
            if (record.RemoteSource is { } remoteSource)
            {
                _ = await CopyRemoteRangeAsync(
                    remoteSource,
                    record.SourceStateId,
                    record.Destination,
                    sourceOffset,
                    destinationOffset,
                    record.Total,
                    copied => record.Copied = copied,
                    record.Token).ConfigureAwait(false);
            }
            else
            {
                await CompleteLocalAsyncCopyAsync(record, sourceOffset, destinationOffset).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            status = Nfs4Status.Delay;
        }
        catch (NfsException ex)
        {
            status = Nfs4StatusMapping.FromStatus(ex.Status);
        }
        catch (Exception ex) when (ex is RpcException or IOException or SocketException)
        {
            status = Nfs4Status.IoError;
        }

        record.Complete = true;
        record.Status = status;
        await SendOffloadCallbackAsync(record).ConfigureAwait(false);
    }

    private async ValueTask CompleteLocalAsyncCopyAsync(
        OffloadRecord record,
        ulong sourceOffset,
        ulong destinationOffset)
    {
        ulong copied = 0;
        using var buffer = new PooledBufferWriter((int)Math.Min((ulong)Nfs4.MaxIoSize, record.Total));
        while (copied < record.Total)
        {
            record.Token.ThrowIfCancellationRequested();
            uint chunk = (uint)Math.Min((ulong)Nfs4.MaxIoSize, record.Total - copied);
            NfsBufferedReadResult read = await _fileSystem
                .ReadAsync(record.Source, sourceOffset + copied, chunk, buffer, record.Token).ConfigureAwait(false);
            ReadOnlyMemory<byte> data = buffer.WrittenMemory;
            if (data.IsEmpty)
            {
                break;
            }

            NfsWriteResult write = await _fileSystem
                .WriteAsync(record.Destination, destinationOffset + copied, data, record.Token).ConfigureAwait(false);
            copied += write.Count;
            record.Copied = copied;
            if (read.EndOfFile || write.Count < read.Count)
            {
                break;
            }

            buffer.Clear();
        }
    }

    private async ValueTask<ulong> CopyRemoteRangeAsync(
        RemoteCopySource source,
        Nfs4StateId sourceStateId,
        NfsFileHandle destination,
        ulong sourceOffset,
        ulong destinationOffset,
        ulong count,
        Action<ulong>? progress,
        CancellationToken cancellationToken)
    {
        ulong copied = 0;
        await using RpcClient rpc = await RpcClient.ConnectAsync(source.EndPoint, cancellationToken).ConfigureAwait(false);
        while (copied < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint chunk = (uint)Math.Min((ulong)Nfs4.MaxIoSize, count - copied);
            Nfs4ReadResult read = await ReadRemoteAsync(
                rpc,
                source.Handle,
                sourceStateId,
                sourceOffset + copied,
                chunk,
                cancellationToken).ConfigureAwait(false);
            if (read.Data.Length == 0)
            {
                break;
            }

            NfsWriteResult write = await _fileSystem
                .WriteAsync(destination, destinationOffset + copied, read.Data, cancellationToken).ConfigureAwait(false);
            copied += write.Count;
            progress?.Invoke(copied);

            if (read.Eof || write.Count < read.Data.Length)
            {
                break;
            }
        }

        return copied;
    }

    private static async ValueTask<Nfs4ReadResult> ReadRemoteAsync(
        RpcClient rpc,
        Nfs4Handle source,
        Nfs4StateId sourceStateId,
        ulong offset,
        uint count,
        CancellationToken cancellationToken)
    {
        var args = new Nfs4CompoundArgs { Tag = "remote-copy-read", MinorVersion = Nfs4.MinorVersion2 };
        args.Operations.Add(new Nfs4PutFhOp { Handle = source });
        args.Operations.Add(new Nfs4ReadOp { StateId = sourceStateId, Offset = offset, Count = count });
        RpcReply reply = await rpc.CallAsync(
            Nfs4.Program,
            Nfs4.ProtocolVersion,
            (uint)Nfs4Procedure.Compound,
            OpaqueAuth.None,
            OpaqueAuth.None,
            args,
            cancellationToken).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new RpcException($"Remote COPY source READ was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }

        Nfs4CompoundResult result = reply.DecodeResult<Nfs4CompoundResult>();
        if (result.Status != Nfs4Status.Ok || result.Operations[^1] is not Nfs4ReadResult read)
        {
            throw new NfsException(NfsStatus.IoError, $"Remote COPY source READ failed with {result.Status}.");
        }

        return read;
    }

    private async ValueTask SendOffloadCallbackAsync(OffloadRecord record)
    {
        if (record.ClientId is not { } clientId ||
            _sessions.NextBackChannelCall(clientId) is not { } backChannel)
        {
            return;
        }

        try
        {
            var response = new Nfs4CopyWriteResponse
            {
                Count = record.Copied,
                Committed = 2,
                Verifier = _writeVerifier,
            };
            _ = await Nfs4CallbackClient.OffloadSessionAsync(
                backChannel.Transport,
                backChannel.Call,
                record.StateId,
                record.Status,
                response,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RpcException or IOException or SocketException)
        {
        }
    }

    private NfsFileHandle? TryGetCopyNotifySource(Nfs4StateId stateId)
    {
        OffloadRecord? record = FindOffload(stateId);
        return record?.Source;
    }

    private Nfs4NetLocation CreateCopyNotifyLocation(NfsFileHandle source)
    {
        IPEndPoint endPoint = _copySourceEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0);
        // Minimal inter-server COPY handshake: the source returns an nfs:// URL carrying its
        // loopback endpoint and the source file handle; the destination then PUTFH+READs it over RPC.
        var builder = new UriBuilder("nfs", endPoint.Address.ToString(), endPoint.Port, "copy")
        {
            Query = "fh=" + Uri.EscapeDataString(Convert.ToBase64String(source.ToArray())),
        };
        return new Nfs4NetLocation
        {
            Type = Nfs4NetLocationType.Url,
            Value = builder.Uri.AbsoluteUri,
        };
    }

    private static bool TryGetRemoteCopySource(
        List<Nfs4NetLocation> sourceServers,
        out RemoteCopySource source)
    {
        foreach (Nfs4NetLocation location in sourceServers)
        {
            if (TryGetRemoteCopySource(location, out source))
            {
                return true;
            }
        }

        source = default;
        return false;
    }

    private static bool TryGetRemoteCopySource(Nfs4NetLocation location, out RemoteCopySource source)
    {
        if (location.Type != Nfs4NetLocationType.Url ||
            !Uri.TryCreate(location.Value, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, "nfs", StringComparison.OrdinalIgnoreCase) ||
            !TryGetIpEndPoint(uri, out IPEndPoint endPoint) ||
            !TryGetQueryValue(uri.Query, "fh", out string encodedHandle))
        {
            source = default;
            return false;
        }

        try
        {
            byte[] handle = Convert.FromBase64String(Uri.UnescapeDataString(encodedHandle));
            source = new RemoteCopySource(endPoint, new Nfs4Handle { Data = handle });
            return true;
        }
        catch (FormatException)
        {
            source = default;
            return false;
        }
    }

    private static bool TryGetIpEndPoint(Uri uri, out IPEndPoint endPoint)
    {
        if (IPAddress.TryParse(uri.Host, out IPAddress? address))
        {
            endPoint = new IPEndPoint(address, uri.Port);
            return true;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            endPoint = new IPEndPoint(IPAddress.Loopback, uri.Port);
            return true;
        }

        endPoint = null!;
        return false;
    }

    private static bool TryGetQueryValue(string query, string name, out string value)
    {
        ReadOnlySpan<char> remaining = query.AsSpan();
        if (!remaining.IsEmpty && remaining[0] == '?')
        {
            remaining = remaining[1..];
        }

        while (!remaining.IsEmpty)
        {
            int separator = remaining.IndexOf('&');
            ReadOnlySpan<char> pair = separator < 0 ? remaining : remaining[..separator];
            int equals = pair.IndexOf('=');
            if (equals >= 0 && pair[..equals].SequenceEqual(name.AsSpan()))
            {
                value = pair[(equals + 1)..].ToString();
                return true;
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
        }

        value = null!;
        return false;
    }

    private Nfs4StateId NewOffloadStateId()
    {
        ulong id = Interlocked.Increment(ref _nextOffloadId);
        byte[] other = new byte[Nfs4.OtherSize];
        BinaryPrimitives.WriteUInt32BigEndian(other.AsSpan(0, 4), OffloadStateIdMagic);
        BinaryPrimitives.WriteUInt64BigEndian(other.AsSpan(4, 8), id);
        return new Nfs4StateId { Sequence = 1, Other = other };
    }

    private static bool IsOffloadStateId(Nfs4StateId stateId) =>
        stateId.Other is { Length: Nfs4.OtherSize } other &&
        BinaryPrimitives.ReadUInt32BigEndian(other.AsSpan(0, 4)) == OffloadStateIdMagic;

    private void StoreOffload(OffloadRecord record)
    {
        lock (_offloadGate)
        {
            _offloads[OffloadKey(record.StateId)] = record;
        }
    }

    private OffloadRecord? FindOffload(Nfs4StateId stateId)
    {
        lock (_offloadGate)
        {
            return _offloads.TryGetValue(OffloadKey(stateId), out OffloadRecord? record) ? record : null;
        }
    }

    private static string OffloadKey(Nfs4StateId stateId) => Convert.ToHexString(stateId.Other ?? []);

    private Nfs4GetDeviceInfoResult GetDeviceInfo(Nfs4GetDeviceInfoOp op)
    {
        if (op.LayoutType != Nfs4LayoutType.Files ||
            !op.DeviceId.AsSpan().SequenceEqual(Nfs4Pnfs.DefaultDeviceId))
        {
            return new Nfs4GetDeviceInfoResult { Status = Nfs4Status.NoSuchDeviceOrAddress };
        }

        var dsAddress = new Nfs4FileLayoutDataServerAddress
        {
            StripeIndices = CreateStripeIndices(),
            MultipathDataServers = CreateMultipathDataServers(),
        };

        return new Nfs4GetDeviceInfoResult
        {
            Status = Nfs4Status.Ok,
            DeviceAddress = new Nfs4DeviceAddress
            {
                LayoutType = Nfs4LayoutType.Files,
                Body = dsAddress.Encode(),
            },
        };
    }

    private Nfs4LayoutGetResult LayoutGet(CompoundContext context, Nfs4LayoutGetOp op)
    {
        if (context.Current is not { } file)
        {
            return new Nfs4LayoutGetResult { Status = Nfs4Status.NoFileHandle };
        }

        if (op.LayoutType != Nfs4LayoutType.Files)
        {
            return new Nfs4LayoutGetResult { Status = Nfs4Status.NotSupported };
        }

        Nfs4Handle handle = Nfs4Mapping.ToWire(file);
        var filesLayout = new Nfs4FileLayout
        {
            DeviceId = Nfs4Pnfs.DefaultDeviceId.ToArray(),
            Util = Nfs4Pnfs.FileLayoutUtilDense,
            StripeUnit = _pnfsOptions.StripeUnit,
            FirstStripeIndex = 0,
            PatternOffset = 0,
            FileHandles = CreateLayoutFileHandles(handle),
        };

        return new Nfs4LayoutGetResult
        {
            Status = Nfs4Status.Ok,
            ReturnOnClose = false,
            StateId = op.StateId,
            Layouts =
            [
                new Nfs4Layout
                {
                    Offset = 0,
                    Length = ulong.MaxValue,
                    Iomode = op.Iomode,
                    Content = new Nfs4LayoutContent
                    {
                        LayoutType = Nfs4LayoutType.Files,
                        Body = filesLayout.Encode(),
                    },
                },
            ],
        };
    }

    private async ValueTask<Nfs4ResOp> LayoutCommitAsync(
        CompoundContext context,
        Nfs4LayoutCommitOp op,
        CancellationToken cancellationToken)
    {
        if (op.LayoutType != Nfs4LayoutType.Files)
        {
            return new Nfs4LayoutCommitResult { Status = Nfs4Status.NotSupported };
        }

        if (context.Current is null)
        {
            return new Nfs4LayoutCommitResult { Status = Nfs4Status.NoFileHandle };
        }

        if (op.NewOffset is not { } newOffset)
        {
            return new Nfs4LayoutCommitResult { Status = Nfs4Status.Ok };
        }

        NfsFileAttributes attributes = await _fileSystem
            .SetAttributesAsync(context.Current.Value, new NfsSetAttributes { Size = newOffset }, cancellationToken)
            .ConfigureAwait(false);
        return new Nfs4LayoutCommitResult { Status = Nfs4Status.Ok, NewSize = attributes.Size };
    }

    private static Nfs4LayoutReturnResult LayoutReturn(Nfs4LayoutReturnOp op) =>
        op.LayoutType == Nfs4LayoutType.Files && op.ReturnType == Nfs4LayoutReturnType.File
            ? new Nfs4LayoutReturnResult { Status = Nfs4Status.Ok }
            : new Nfs4LayoutReturnResult { Status = Nfs4Status.NotSupported };

    private uint[] CreateStripeIndices()
    {
        uint[] indices = new uint[checked((int)_pnfsOptions.StripeCount)];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)i % _pnfsOptions.DataServerCount;
        }

        return indices;
    }

    private Nfs4NetAddress[][] CreateMultipathDataServers()
    {
        var dataServers = new Nfs4NetAddress[_pnfsOptions.DataServerUniversalAddresses.Count][];
        for (int i = 0; i < dataServers.Length; i++)
        {
            dataServers[i] =
            [
                new Nfs4NetAddress
                {
                    NetId = "tcp",
                    Uaddr = _pnfsOptions.DataServerUniversalAddresses[i],
                },
            ];
        }

        return dataServers;
    }

    private Nfs4Handle[] CreateLayoutFileHandles(Nfs4Handle handle)
    {
        Nfs4Handle[] handles = new Nfs4Handle[checked((int)_pnfsOptions.DataServerCount)];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = new Nfs4Handle { Data = handle.Data.ToArray() };
        }

        return handles;
    }

    private Nfs4CreateSessionResult CreateSession(Nfs4CreateSessionOp op, CompoundContext context)
    {
        INfs41BackChannelTransport? transport = _backChannelTransport;
        if (transport is null &&
            (op.Flags & Nfs4CreateSessionOp.FlagConnectionBackChannel) != 0 &&
            op.CallbackProgram != 0 &&
            context.Connection is not null)
        {
            transport = new Nfs41ConnectionBackChannelTransport(context.Connection);
        }

        byte[]? sessionId = _sessions.CreateSession(
            op.ClientId,
            op.ForeChannel.MaxRequests,
            op.BackChannel.MaxRequests,
            op.Flags,
            op.CallbackProgram,
            transport);
        if (sessionId is null)
        {
            return new Nfs4CreateSessionResult { Status = Nfs4Status.StaleClientId };
        }

        if (_sessions.GetClientOwner(op.ClientId) is { } owner)
        {
            _state.RegisterSessionClient(op.ClientId, owner);
        }

        return new Nfs4CreateSessionResult
        {
            Status = Nfs4Status.Ok,
            SessionId = sessionId,
            Sequence = op.Sequence,
            Flags = op.Flags,
            ForeChannel = op.ForeChannel,
            BackChannel = op.BackChannel,
        };
    }

    private Nfs4StatusResult DestroySession(Nfs4DestroySessionOp op) => new(Nfs4Op.DestroySession)
    {
        Status = _sessions.DestroySession(op.SessionId) ? Nfs4Status.Ok : Nfs4Status.BadSession,
    };

    private Nfs4StatusResult DestroyClientId(Nfs4DestroyClientIdOp op) => new(Nfs4Op.DestroyClientId)
    {
        Status = _sessions.DestroyClientId(op.ClientId) ? Nfs4Status.Ok : Nfs4Status.StaleClientId,
    };

    private Nfs4StatusResult ReclaimComplete()
    {
        _state.CompleteReclaim();
        return new Nfs4StatusResult(Nfs4Op.ReclaimComplete) { Status = Nfs4Status.Ok };
    }

    private async ValueTask<NfsFileHandle?> TryLookupAsync(
        NfsFileHandle directory,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _fileSystem.LookupAsync(directory, name, cancellationToken).ConfigureAwait(false);
        }
        catch (NfsException)
        {
            return null;
        }
    }

    private async ValueTask<Nfs4FAttr> EncodeCurrentAttributesAsync(
        NfsFileHandle current,
        Nfs4Bitmap requested,
        CancellationToken cancellationToken)
    {
        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(current, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NfsAccessControlEntry>? acl = requested.IsSet(Nfs4AttributeId.Acl)
            ? await _fileSystem.GetAccessControlListAsync(current, cancellationToken).ConfigureAwait(false)
            : null;
        return Nfs4Mapping.BuildAttributes(attributes, current, acl).Encode(requested);
    }

    private async ValueTask<Nfs4Bitmap> ApplyCreateAttributesAsync(
        NfsFileHandle file,
        Nfs4FAttr attributes,
        CancellationToken cancellationToken)
    {
        (NfsSetAttributes changes, Nfs4Bitmap applied) = Nfs4Mapping.ToSetAttributes(attributes);
        if (applied == Nfs4Bitmap.Empty)
        {
            return Nfs4Bitmap.Empty;
        }

        try
        {
            await _fileSystem.SetAttributesAsync(file, changes, cancellationToken).ConfigureAwait(false);
            return applied;
        }
        catch (NfsException)
        {
            return Nfs4Bitmap.Empty;
        }
    }

    private async ValueTask<ulong> ChangeOfAsync(NfsFileHandle handle, CancellationToken cancellationToken)
    {
        NfsFileAttributes attributes = await _fileSystem
            .GetAttributesAsync(handle, cancellationToken).ConfigureAwait(false);
        return Nfs4Mapping.ChangeId(attributes);
    }

    private bool HasExclusiveCreateVerifier(NfsFileHandle file, byte[] verifier)
    {
        lock (_exclusiveCreateGate)
        {
            return _exclusiveCreateVerifiers.TryGetValue(Convert.ToHexString(file.Span), out byte[]? existing) &&
                existing.AsSpan().SequenceEqual(verifier ?? []);
        }
    }

    private void RememberExclusiveCreateVerifier(NfsFileHandle file, byte[] verifier)
    {
        lock (_exclusiveCreateGate)
        {
            _exclusiveCreateVerifiers[Convert.ToHexString(file.Span)] = (byte[])(verifier ?? []).Clone();
        }
    }

    private static bool AreSameAttributes(Nfs4FAttr left, Nfs4FAttr right) =>
        left.Mask.Equals(right.Mask) && (left.Values ?? []).AsSpan().SequenceEqual(right.Values ?? []);

    private static bool IsAnonymousStateId(Nfs4StateId stateId) =>
        stateId.Sequence == 0 && (stateId.Other ?? []).AsSpan().SequenceEqual(Nfs4StateId.Anonymous.Other);

    private static Nfs4ResOp Failed(Nfs4Op op, Nfs4Status status) => op switch
    {
        Nfs4Op.GetFh => new Nfs4GetFhResult { Status = status },
        Nfs4Op.GetAttr => new Nfs4GetAttrResult { Status = status },
        Nfs4Op.Verify => new Nfs4StatusResult(Nfs4Op.Verify) { Status = status },
        Nfs4Op.NVerify => new Nfs4StatusResult(Nfs4Op.NVerify) { Status = status },
        Nfs4Op.Access => new Nfs4AccessResult { Status = status },
        Nfs4Op.SecInfo => new Nfs4SecInfoResult(Nfs4Op.SecInfo) { Status = status },
        Nfs4Op.SecInfoNoName => new Nfs4SecInfoResult(Nfs4Op.SecInfoNoName) { Status = status },
        Nfs4Op.Read => new Nfs4ReadResult { Status = status },
        Nfs4Op.Write => new Nfs4WriteResult { Status = status },
        Nfs4Op.ReadLink => new Nfs4ReadLinkResult { Status = status },
        Nfs4Op.ReadDir => new Nfs4ReadDirResult { Status = status },
        Nfs4Op.Remove => new Nfs4RemoveResult { Status = status },
        Nfs4Op.Rename => new Nfs4RenameResult { Status = status },
        Nfs4Op.Create => new Nfs4CreateResult { Status = status },
        Nfs4Op.SetAttr => new Nfs4SetAttrResult { Status = status },
        Nfs4Op.SetClientId => new Nfs4SetClientIdResult { Status = status },
        Nfs4Op.Open => new Nfs4OpenResult { Status = status },
        Nfs4Op.OpenConfirm => new Nfs4StateIdResult(Nfs4Op.OpenConfirm) { Status = status },
        Nfs4Op.OpenDowngrade => new Nfs4StateIdResult(Nfs4Op.OpenDowngrade) { Status = status },
        Nfs4Op.Close => new Nfs4StateIdResult(Nfs4Op.Close) { Status = status },
        Nfs4Op.Lock => new Nfs4LockResult { Status = status },
        Nfs4Op.LockTest => new Nfs4LockTestResult { Status = status },
        Nfs4Op.LockUnlock => new Nfs4LockUnlockResult { Status = status },
        Nfs4Op.BindConnToSession => new Nfs4BindConnToSessionResult { Status = status },
        Nfs4Op.ExchangeId => new Nfs4ExchangeIdResult { Status = status },
        Nfs4Op.CreateSession => new Nfs4CreateSessionResult { Status = status },
        Nfs4Op.Sequence => new Nfs4SequenceResult { Status = status },
        Nfs4Op.TestStateId => new Nfs4TestStateIdResult { Status = status },
        Nfs4Op.GetDeviceInfo => new Nfs4GetDeviceInfoResult { Status = status },
        Nfs4Op.LayoutGet => new Nfs4LayoutGetResult { Status = status },
        Nfs4Op.LayoutCommit => new Nfs4LayoutCommitResult { Status = status },
        Nfs4Op.LayoutReturn => new Nfs4LayoutReturnResult { Status = status },
        Nfs4Op.Copy => new Nfs4CopyResult { Status = status },
        Nfs4Op.CopyNotify => new Nfs4CopyNotifyResult { Status = status },
        Nfs4Op.OffloadStatus => new Nfs4OffloadStatusResult { Status = status },
        Nfs4Op.ReadPlus => new Nfs4ReadPlusResult { Status = status },
        Nfs4Op.Seek => new Nfs4SeekResult { Status = status },
        Nfs4Op.GetXattr => new Nfs4GetXattrResult { Status = status },
        Nfs4Op.SetXattr => new Nfs4SetXattrResult { Status = status },
        Nfs4Op.ListXattrs => new Nfs4ListXattrsResult { Status = status },
        Nfs4Op.RemoveXattr => new Nfs4RemoveXattrResult { Status = status },
        _ => new Nfs4StatusResult(op) { Status = status },
    };

    private CompoundContext CreateContext(RpcCallInfo request) => new()
    {
        AdvertiseRpcSecGss = _rpcSecGssEnabled ||
            request.Credential.Flavor == AuthFlavor.RpcSecGss ||
            request.RpcSecGss is not null,
        Connection = request.Connection,
    };

    private static Nfs4SecInfo[] BuildSecurityFlavors(bool includeRpcSecGss)
    {
        if (!includeRpcSecGss)
        {
            return [Nfs4SecInfo.AuthNone, Nfs4SecInfo.AuthSys];
        }

        return
        [
            Nfs4SecInfo.AuthNone,
            Nfs4SecInfo.AuthSys,
            Nfs4SecInfo.RpcGss(new Nfs4RpcSecGssInfo(KerberosV5Oid, 0, Nfs4RpcGssService.None)),
            Nfs4SecInfo.RpcGss(new Nfs4RpcSecGssInfo(KerberosV5Oid, 0, Nfs4RpcGssService.Integrity)),
            Nfs4SecInfo.RpcGss(new Nfs4RpcSecGssInfo(KerberosV5Oid, 0, Nfs4RpcGssService.Privacy)),
        ];
    }

    private static Nfs4CompoundArgs DecodeCompound(ReadOnlyMemory<byte> arguments)
    {
        var reader = new XdrReader(arguments.Span);
        return Nfs4CompoundArgs.Decode(ref reader);
    }

    private static RpcReplyPayload EncodeCompound(Nfs4CompoundResult result) =>
        RpcReplyPayload.Success(EncodeBytes(result));

    private static byte[] EncodeBytes(Nfs4CompoundResult result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.Encode(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    private sealed class CompoundContext
    {
        public NfsFileHandle? Current { get; set; }

        public NfsFileHandle? Saved { get; set; }

        public bool AdvertiseRpcSecGss { get; init; }

        public RpcDuplexConnection? Connection { get; init; }

        public byte[]? SessionId { get; set; }

        public ulong? SessionClientId { get; set; }
    }

    private readonly record struct RemoteCopySource(IPEndPoint EndPoint, Nfs4Handle Handle);

    private sealed class OffloadRecord(
        Nfs4StateId stateId,
        NfsFileHandle source,
        NfsFileHandle destination,
        ulong total,
        long createdAt,
        CancellationTokenSource? cancellation,
        RemoteCopySource? remoteSource)
    {
        public Nfs4StateId StateId { get; } = stateId;

        public NfsFileHandle Source { get; } = source;

        public NfsFileHandle Destination { get; } = destination;

        public ulong Total { get; } = total;

        public long CreatedAt { get; } = createdAt;

        public ulong Copied { get; set; }

        public bool Complete { get; set; }

        public Nfs4Status Status { get; set; } = Nfs4Status.Ok;

        public ulong? ClientId { get; set; }

        public Nfs4StateId SourceStateId { get; set; } = Nfs4StateId.Anonymous;

        public RemoteCopySource? RemoteSource { get; } = remoteSource;

        public CancellationToken Token => cancellation?.Token ?? CancellationToken.None;

        public void Cancel()
        {
            cancellation?.Cancel();
            Status = Nfs4Status.Delay;
        }
    }
}
