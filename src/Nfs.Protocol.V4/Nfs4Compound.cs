using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>A decoded COMPOUND request (<c>COMPOUND4args</c>, RFC 7530).</summary>
public sealed class Nfs4CompoundArgs : IXdrSerializable<Nfs4CompoundArgs>
{
    /// <summary>Gets or sets the human-readable request tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets or sets the protocol minor version.</summary>
    public uint MinorVersion { get; set; }

    /// <summary>Gets the operations to execute, in order.</summary>
    public List<Nfs4ArgOp> Operations { get; } = [];

    /// <summary>The largest number of operations a single COMPOUND may contain.</summary>
    public const int MaxOperations = 4096;

    /// <summary>Encodes the request.</summary>
    /// <param name="writer">The writer.</param>
    public void Encode(ref XdrWriter writer)
    {
        writer.WriteString(Tag);
        writer.WriteUInt32(MinorVersion);
        writer.WriteUInt32((uint)Operations.Count);
        foreach (Nfs4ArgOp operation in Operations)
        {
            writer.WriteInt32((int)operation.Op);
            operation.Encode(ref writer);
        }
    }

    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer) => Encode(ref writer);

    /// <inheritdoc/>
    public static Nfs4CompoundArgs ReadFrom(ref XdrReader reader) => Decode(ref reader);

    /// <summary>Decodes a request from <paramref name="reader"/>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded request.</returns>
    public static Nfs4CompoundArgs Decode(ref XdrReader reader)
    {
        var args = new Nfs4CompoundArgs
        {
            Tag = reader.ReadString(1024),
            MinorVersion = reader.ReadUInt32(),
        };

        uint count = reader.ReadUInt32();
        if (count > MaxOperations)
        {
            throw new XdrException("COMPOUND operation count is implausibly large.");
        }

        for (uint i = 0; i < count; i++)
        {
            var op = (Nfs4Op)reader.ReadInt32();
            args.Operations.Add(DecodeArgOp(op, ref reader));
        }

        return args;
    }

    private static Nfs4ArgOp DecodeArgOp(Nfs4Op op, ref XdrReader reader) => op switch
    {
        Nfs4Op.PutRootFh => new Nfs4PutRootFhOp(),
        Nfs4Op.GetFh => new Nfs4GetFhOp(),
        Nfs4Op.SaveFh => new Nfs4SaveFhOp(),
        Nfs4Op.RestoreFh => new Nfs4RestoreFhOp(),
        Nfs4Op.LookupParent => new Nfs4LookupParentOp(),
        Nfs4Op.ReadLink => new Nfs4ReadLinkOp(),
        Nfs4Op.PutFh => new Nfs4PutFhOp { Handle = Nfs4Handle.ReadFrom(ref reader) },
        Nfs4Op.Lookup => new Nfs4LookupOp { Name = reader.ReadString(Nfs4.MaxNameLength) },
        Nfs4Op.SecInfo => new Nfs4SecInfoOp { Name = reader.ReadString(Nfs4.MaxNameLength) },
        Nfs4Op.SecInfoNoName => new Nfs4SecInfoNoNameOp
        {
            Style = (Nfs4SecInfoStyle)reader.ReadUInt32(),
        },
        Nfs4Op.GetAttr => new Nfs4GetAttrOp { Request = Nfs4Bitmap.ReadFrom(ref reader) },
        Nfs4Op.Access => new Nfs4AccessOp { Access = reader.ReadUInt32() },
        Nfs4Op.Read => DecodeRead(ref reader),
        Nfs4Op.Write => DecodeWrite(ref reader),
        Nfs4Op.ReadDir => DecodeReadDir(ref reader),
        Nfs4Op.Remove => new Nfs4RemoveOp { Name = reader.ReadString(Nfs4.MaxNameLength) },
        Nfs4Op.Rename => DecodeRename(ref reader),
        Nfs4Op.SetAttr => DecodeSetAttr(ref reader),
        Nfs4Op.Create => DecodeCreate(ref reader),
        Nfs4Op.SetClientId => DecodeSetClientId(ref reader),
        Nfs4Op.SetClientIdConfirm => new Nfs4SetClientIdConfirmOp
        {
            ClientId = reader.ReadUInt64(),
            Confirm = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray(),
        },
        Nfs4Op.DelegReturn => new Nfs4DelegReturnOp { StateId = Nfs4StateId.ReadFrom(ref reader) },
        Nfs4Op.Open => DecodeOpen(ref reader),
        Nfs4Op.OpenConfirm => new Nfs4OpenConfirmOp
        {
            OpenStateId = Nfs4StateId.ReadFrom(ref reader),
            Seqid = reader.ReadUInt32(),
        },
        Nfs4Op.Close => new Nfs4CloseOp
        {
            Seqid = reader.ReadUInt32(),
            OpenStateId = Nfs4StateId.ReadFrom(ref reader),
        },
        Nfs4Op.Renew => new Nfs4RenewOp { ClientId = reader.ReadUInt64() },
        Nfs4Op.Lock => DecodeLock(ref reader),
        Nfs4Op.LockTest => DecodeLockTest(ref reader),
        Nfs4Op.LockUnlock => DecodeLockUnlock(ref reader),
        Nfs4Op.ExchangeId => DecodeExchangeId(ref reader),
        Nfs4Op.CreateSession => DecodeCreateSession(ref reader),
        Nfs4Op.Sequence => DecodeSequence(ref reader),
        Nfs4Op.GetDeviceInfo => new Nfs4GetDeviceInfoOp
        {
            DeviceId = reader.ReadOpaqueFixed(Nfs4Pnfs.DeviceIdSize).ToArray(),
            LayoutType = (Nfs4LayoutType)reader.ReadUInt32(),
            MaxCount = reader.ReadUInt32(),
            NotifyTypes = Nfs4Bitmap.ReadFrom(ref reader),
        },
        Nfs4Op.LayoutGet => new Nfs4LayoutGetOp
        {
            SignalLayoutAvailable = reader.ReadBool(),
            LayoutType = (Nfs4LayoutType)reader.ReadUInt32(),
            Iomode = (Nfs4LayoutIomode)reader.ReadUInt32(),
            Offset = reader.ReadUInt64(),
            Length = reader.ReadUInt64(),
            MinLength = reader.ReadUInt64(),
            StateId = Nfs4StateId.ReadFrom(ref reader),
            MaxCount = reader.ReadUInt32(),
        },
        Nfs4Op.LayoutCommit => DecodeLayoutCommit(ref reader),
        Nfs4Op.LayoutReturn => DecodeLayoutReturn(ref reader),
        Nfs4Op.DestroySession => new Nfs4DestroySessionOp
        {
            SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray(),
        },
        Nfs4Op.DestroyClientId => new Nfs4DestroyClientIdOp { ClientId = reader.ReadUInt64() },
        Nfs4Op.ReclaimComplete => new Nfs4ReclaimCompleteOp { OneFileSystem = reader.ReadBool() },
        Nfs4Op.Copy => DecodeCopy(ref reader),
        Nfs4Op.CopyNotify => new Nfs4CopyNotifyOp
        {
            SourceStateId = Nfs4StateId.ReadFrom(ref reader),
            Destination = Nfs4NetLocation.ReadFrom(ref reader),
        },
        Nfs4Op.Allocate => new Nfs4AllocateOp
        {
            StateId = Nfs4StateId.ReadFrom(ref reader),
            Offset = reader.ReadUInt64(),
            Length = reader.ReadUInt64(),
        },
        Nfs4Op.Deallocate => new Nfs4DeallocateOp
        {
            StateId = Nfs4StateId.ReadFrom(ref reader),
            Offset = reader.ReadUInt64(),
            Length = reader.ReadUInt64(),
        },
        Nfs4Op.Seek => new Nfs4SeekOp
        {
            StateId = Nfs4StateId.ReadFrom(ref reader),
            Offset = reader.ReadUInt64(),
            What = (Nfs4ContentType)reader.ReadUInt32(),
        },
        Nfs4Op.ReadPlus => new Nfs4ReadPlusOp
        {
            StateId = Nfs4StateId.ReadFrom(ref reader),
            Offset = reader.ReadUInt64(),
            Count = reader.ReadUInt32(),
        },
        Nfs4Op.Clone => new Nfs4CloneOp
        {
            SourceStateId = Nfs4StateId.ReadFrom(ref reader),
            DestinationStateId = Nfs4StateId.ReadFrom(ref reader),
            SourceOffset = reader.ReadUInt64(),
            DestinationOffset = reader.ReadUInt64(),
            Count = reader.ReadUInt64(),
        },
        Nfs4Op.GetXattr => new Nfs4GetXattrOp { Name = reader.ReadString(Nfs4.MaxNameLength) },
        Nfs4Op.SetXattr => new Nfs4SetXattrOp
        {
            Option = (Nfs4SetXattrOption)reader.ReadUInt32(),
            Name = reader.ReadString(Nfs4.MaxNameLength),
            Value = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray(),
        },
        Nfs4Op.ListXattrs => new Nfs4ListXattrsOp
        {
            Cookie = reader.ReadUInt64(),
            MaxCount = reader.ReadUInt32(),
        },
        Nfs4Op.RemoveXattr => new Nfs4RemoveXattrOp { Name = reader.ReadString(Nfs4.MaxNameLength) },
        Nfs4Op.OffloadCancel => new Nfs4OffloadCancelOp { StateId = Nfs4StateId.ReadFrom(ref reader) },
        Nfs4Op.OffloadStatus => new Nfs4OffloadStatusOp { StateId = Nfs4StateId.ReadFrom(ref reader) },
        _ => throw new XdrException($"Unsupported NFSv4 operation {(int)op}."),
    };

    private static Nfs4CopyOp DecodeCopy(ref XdrReader reader)
    {
        var op = new Nfs4CopyOp
        {
            SourceStateId = Nfs4StateId.ReadFrom(ref reader),
            DestinationStateId = Nfs4StateId.ReadFrom(ref reader),
            SourceOffset = reader.ReadUInt64(),
            DestinationOffset = reader.ReadUInt64(),
            Count = reader.ReadUInt64(),
            Consecutive = reader.ReadBool(),
            Synchronous = reader.ReadBool(),
        };

        uint sourceServerCount = reader.ReadUInt32();
        if (sourceServerCount > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("COPY source_server count is implausibly large.");
        }

        for (uint i = 0; i < sourceServerCount; i++)
        {
            op.SourceServers.Add(Nfs4NetLocation.ReadFrom(ref reader));
        }

        return op;
    }

    private static Nfs4LayoutCommitOp DecodeLayoutCommit(ref XdrReader reader)
    {
        var op = new Nfs4LayoutCommitOp
        {
            Offset = reader.ReadUInt64(),
            Length = reader.ReadUInt64(),
            Reclaim = reader.ReadBool(),
            StateId = Nfs4StateId.ReadFrom(ref reader),
        };

        if (reader.ReadBool())
        {
            op.NewOffset = reader.ReadUInt64();
        }

        if (reader.ReadBool())
        {
            op.NewTime = Nfs4Time.ReadFrom(ref reader);
        }

        op.LayoutType = (Nfs4LayoutType)reader.ReadUInt32();
        op.LayoutUpdate = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray();
        return op;
    }

    private static Nfs4LayoutReturnOp DecodeLayoutReturn(ref XdrReader reader)
    {
        var op = new Nfs4LayoutReturnOp
        {
            Reclaim = reader.ReadBool(),
            LayoutType = (Nfs4LayoutType)reader.ReadUInt32(),
            Iomode = (Nfs4LayoutIomode)reader.ReadUInt32(),
            ReturnType = (Nfs4LayoutReturnType)reader.ReadUInt32(),
        };

        if (op.ReturnType == Nfs4LayoutReturnType.File)
        {
            op.Offset = reader.ReadUInt64();
            op.Length = reader.ReadUInt64();
            op.StateId = Nfs4StateId.ReadFrom(ref reader);
            op.Body = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray();
        }

        return op;
    }

    private static Nfs4ExchangeIdOp DecodeExchangeId(ref XdrReader reader)
    {
        byte[] verifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray();
        byte[] ownerId = reader.ReadOpaqueVariable(1024).ToArray();
        uint flags = reader.ReadUInt32();
        uint stateProtect = reader.ReadUInt32();
        if (stateProtect != 0)
        {
            throw new XdrException("Only SP4_NONE state protection is supported.");
        }

        uint implCount = reader.ReadUInt32();
        for (uint i = 0; i < implCount; i++)
        {
            _ = reader.ReadOpaqueVariable(1024); // nii_domain
            _ = reader.ReadOpaqueVariable(1024); // nii_name
            _ = reader.ReadInt64();              // nii_date.seconds
            _ = reader.ReadUInt32();             // nii_date.nseconds
        }

        return new Nfs4ExchangeIdOp { Verifier = verifier, OwnerId = ownerId, Flags = flags };
    }

    private static Nfs4CreateSessionOp DecodeCreateSession(ref XdrReader reader)
    {
        var op = new Nfs4CreateSessionOp
        {
            ClientId = reader.ReadUInt64(),
            Sequence = reader.ReadUInt32(),
            Flags = reader.ReadUInt32(),
            ForeChannel = Nfs4ChannelAttributes.ReadFrom(ref reader),
            BackChannel = Nfs4ChannelAttributes.ReadFrom(ref reader),
            CallbackProgram = reader.ReadUInt32(),
        };

        op.CallbackSecurityFlavors.Clear();
        uint secCount = reader.ReadUInt32();
        for (uint i = 0; i < secCount; i++)
        {
            int flavor = reader.ReadInt32();
            op.CallbackSecurityFlavors.Add(flavor);
            if (flavor == 1) // AUTH_SYS callback security: skip the authsys_parms body.
            {
                _ = reader.ReadUInt32();                 // stamp
                _ = reader.ReadString(255);              // machinename
                _ = reader.ReadUInt32();                 // uid
                _ = reader.ReadUInt32();                 // gid
                uint gids = reader.ReadUInt32();
                for (uint g = 0; g < gids; g++)
                {
                    _ = reader.ReadUInt32();
                }
            }
        }

        return op;
    }

    private static Nfs4SequenceOp DecodeSequence(ref XdrReader reader) => new()
    {
        SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray(),
        SequenceId = reader.ReadUInt32(),
        SlotId = reader.ReadUInt32(),
        HighestSlotId = reader.ReadUInt32(),
        CacheThis = reader.ReadBool(),
    };

    private static Nfs4LockOp DecodeLock(ref XdrReader reader)
    {
        var lockType = (Nfs4LockType)reader.ReadUInt32();
        bool reclaim = reader.ReadBool();
        ulong offset = reader.ReadUInt64();
        ulong length = reader.ReadUInt64();
        bool newLockOwner = reader.ReadBool();

        var op = new Nfs4LockOp
        {
            LockType = lockType,
            Reclaim = reclaim,
            Offset = offset,
            Length = length,
            NewLockOwner = newLockOwner,
        };

        if (newLockOwner)
        {
            op.OpenSeqid = reader.ReadUInt32();
            op.OpenStateId = Nfs4StateId.ReadFrom(ref reader);
            op.LockSeqid = reader.ReadUInt32();
            ulong clientId = reader.ReadUInt64();
            byte[] owner = reader.ReadOpaqueVariable(1024).ToArray();
            op.LockOwner = new Nfs4LockOwner(clientId, owner);
        }
        else
        {
            op.LockStateId = Nfs4StateId.ReadFrom(ref reader);
            op.LockSeqid = reader.ReadUInt32();
        }

        return op;
    }

    private static Nfs4LockTestOp DecodeLockTest(ref XdrReader reader)
    {
        var lockType = (Nfs4LockType)reader.ReadUInt32();
        ulong offset = reader.ReadUInt64();
        ulong length = reader.ReadUInt64();
        ulong clientId = reader.ReadUInt64();
        byte[] owner = reader.ReadOpaqueVariable(1024).ToArray();
        return new Nfs4LockTestOp
        {
            LockType = lockType,
            Offset = offset,
            Length = length,
            Owner = new Nfs4LockOwner(clientId, owner),
        };
    }

    private static Nfs4LockUnlockOp DecodeLockUnlock(ref XdrReader reader) => new()
    {
        LockType = (Nfs4LockType)reader.ReadUInt32(),
        Seqid = reader.ReadUInt32(),
        LockStateId = Nfs4StateId.ReadFrom(ref reader),
        Offset = reader.ReadUInt64(),
        Length = reader.ReadUInt64(),
    };

    private static Nfs4SetClientIdOp DecodeSetClientId(ref XdrReader reader) => new()
    {
        Verifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray(),
        Id = reader.ReadOpaqueVariable(1024).ToArray(),
        CallbackProgram = reader.ReadUInt32(),
        CallbackNetId = reader.ReadString(256),
        CallbackAddress = reader.ReadString(256),
        CallbackIdent = reader.ReadUInt32(),
    };

    private static Nfs4OpenOp DecodeOpen(ref XdrReader reader)
    {
        uint seqid = reader.ReadUInt32();
        uint shareAccess = reader.ReadUInt32();
        uint shareDeny = reader.ReadUInt32();
        ulong clientId = reader.ReadUInt64();
        byte[] owner = reader.ReadOpaqueVariable(1024).ToArray();

        var openType = (Nfs4OpenType)reader.ReadUInt32();
        var createMode = Nfs4CreateMode.Unchecked;
        Nfs4FAttr createAttributes = default;
        if (openType == Nfs4OpenType.Create)
        {
            createMode = (Nfs4CreateMode)reader.ReadUInt32();
            if (createMode == Nfs4CreateMode.Exclusive)
            {
                throw new XdrException("EXCLUSIVE4 create is not supported.");
            }

            createAttributes = Nfs4FAttr.ReadFrom(ref reader);
        }

        uint claimType = reader.ReadUInt32();
        bool reclaim = claimType == 1;
        string name = string.Empty;
        if (claimType == 0)
        {
            name = reader.ReadString(Nfs4.MaxNameLength);
        }
        else if (reclaim)
        {
            _ = reader.ReadUInt32(); // delegate_type
        }
        else
        {
            throw new XdrException("Only CLAIM_NULL and CLAIM_PREVIOUS opens are supported.");
        }

        return new Nfs4OpenOp
        {
            Seqid = seqid,
            ShareAccess = shareAccess,
            ShareDeny = shareDeny,
            ClientId = clientId,
            Owner = owner,
            OpenType = openType,
            CreateMode = createMode,
            CreateAttributes = createAttributes,
            Name = name,
            Reclaim = reclaim,
        };
    }

    private static Nfs4ReadOp DecodeRead(ref XdrReader reader) => new()
    {
        StateId = Nfs4StateId.ReadFrom(ref reader),
        Offset = reader.ReadUInt64(),
        Count = reader.ReadUInt32(),
    };

    private static Nfs4WriteOp DecodeWrite(ref XdrReader reader) => new()
    {
        StateId = Nfs4StateId.ReadFrom(ref reader),
        Offset = reader.ReadUInt64(),
        Stable = reader.ReadUInt32(),
        Data = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray(),
    };

    private static Nfs4ReadDirOp DecodeReadDir(ref XdrReader reader) => new()
    {
        Cookie = reader.ReadUInt64(),
        CookieVerifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray(),
        DirectoryCount = reader.ReadUInt32(),
        MaxCount = reader.ReadUInt32(),
        Request = Nfs4Bitmap.ReadFrom(ref reader),
    };

    private static Nfs4RenameOp DecodeRename(ref XdrReader reader) => new()
    {
        OldName = reader.ReadString(Nfs4.MaxNameLength),
        NewName = reader.ReadString(Nfs4.MaxNameLength),
    };

    private static Nfs4SetAttrOp DecodeSetAttr(ref XdrReader reader) => new()
    {
        StateId = Nfs4StateId.ReadFrom(ref reader),
        Attributes = Nfs4FAttr.ReadFrom(ref reader),
    };

    private static Nfs4CreateOp DecodeCreate(ref XdrReader reader)
    {
        var type = (Nfs4CreateType)reader.ReadUInt32();
        string linkTarget = type == Nfs4CreateType.SymbolicLink ? reader.ReadString(Nfs4.MaxIoSize) : string.Empty;
        return new Nfs4CreateOp
        {
            Type = type,
            LinkTarget = linkTarget,
            Name = reader.ReadString(Nfs4.MaxNameLength),
            Attributes = Nfs4FAttr.ReadFrom(ref reader),
        };
    }
}

/// <summary>A decoded COMPOUND reply (<c>COMPOUND4res</c>, RFC 7530).</summary>
public sealed class Nfs4CompoundResult : IXdrSerializable<Nfs4CompoundResult>
{
    /// <summary>Gets or sets the overall status (the status of the last operation executed).</summary>
    public Nfs4Status Status { get; set; }

    /// <summary>Gets or sets the request tag echoed back.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets the operation results, in order.</summary>
    public List<Nfs4ResOp> Operations { get; } = [];

    /// <summary>Encodes the reply.</summary>
    /// <param name="writer">The writer.</param>
    public void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        writer.WriteString(Tag);
        writer.WriteUInt32((uint)Operations.Count);
        foreach (Nfs4ResOp operation in Operations)
        {
            writer.WriteInt32((int)operation.Op);
            operation.Encode(ref writer);
        }
    }

    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer) => Encode(ref writer);

    /// <inheritdoc/>
    public static Nfs4CompoundResult ReadFrom(ref XdrReader reader) => Decode(ref reader);

    /// <summary>Decodes a reply from <paramref name="reader"/>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded reply.</returns>
    public static Nfs4CompoundResult Decode(ref XdrReader reader)
    {
        var result = new Nfs4CompoundResult
        {
            Status = (Nfs4Status)reader.ReadInt32(),
            Tag = reader.ReadString(1024),
        };

        uint count = reader.ReadUInt32();
        if (count > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("COMPOUND result count is implausibly large.");
        }

        for (uint i = 0; i < count; i++)
        {
            var op = (Nfs4Op)reader.ReadInt32();
            Nfs4ResOp resop = CreateResOp(op);
            resop.Decode(ref reader);
            result.Operations.Add(resop);
        }

        return result;
    }

    private static Nfs4ResOp CreateResOp(Nfs4Op op) => op switch
    {
        Nfs4Op.GetFh => new Nfs4GetFhResult(),
        Nfs4Op.GetAttr => new Nfs4GetAttrResult(),
        Nfs4Op.Access => new Nfs4AccessResult(),
        Nfs4Op.SecInfo => new Nfs4SecInfoResult(Nfs4Op.SecInfo),
        Nfs4Op.SecInfoNoName => new Nfs4SecInfoResult(Nfs4Op.SecInfoNoName),
        Nfs4Op.Read => new Nfs4ReadResult(),
        Nfs4Op.Write => new Nfs4WriteResult(),
        Nfs4Op.ReadLink => new Nfs4ReadLinkResult(),
        Nfs4Op.ReadDir => new Nfs4ReadDirResult(),
        Nfs4Op.Remove => new Nfs4RemoveResult(),
        Nfs4Op.Rename => new Nfs4RenameResult(),
        Nfs4Op.Create => new Nfs4CreateResult(),
        Nfs4Op.SetAttr => new Nfs4SetAttrResult(),
        Nfs4Op.SetClientId => new Nfs4SetClientIdResult(),
        Nfs4Op.Open => new Nfs4OpenResult(),
        Nfs4Op.OpenConfirm => new Nfs4StateIdResult(Nfs4Op.OpenConfirm),
        Nfs4Op.Close => new Nfs4StateIdResult(Nfs4Op.Close),
        Nfs4Op.Lock => new Nfs4LockResult(),
        Nfs4Op.LockTest => new Nfs4LockTestResult(),
        Nfs4Op.LockUnlock => new Nfs4LockUnlockResult(),
        Nfs4Op.ExchangeId => new Nfs4ExchangeIdResult(),
        Nfs4Op.CreateSession => new Nfs4CreateSessionResult(),
        Nfs4Op.Sequence => new Nfs4SequenceResult(),
        Nfs4Op.GetDeviceInfo => new Nfs4GetDeviceInfoResult(),
        Nfs4Op.LayoutGet => new Nfs4LayoutGetResult(),
        Nfs4Op.LayoutCommit => new Nfs4LayoutCommitResult(),
        Nfs4Op.LayoutReturn => new Nfs4LayoutReturnResult(),
        Nfs4Op.Copy => new Nfs4CopyResult(),
        Nfs4Op.CopyNotify => new Nfs4CopyNotifyResult(),
        Nfs4Op.OffloadStatus => new Nfs4OffloadStatusResult(),
        Nfs4Op.ReadPlus => new Nfs4ReadPlusResult(),
        Nfs4Op.Seek => new Nfs4SeekResult(),
        Nfs4Op.GetXattr => new Nfs4GetXattrResult(),
        Nfs4Op.SetXattr => new Nfs4SetXattrResult(),
        Nfs4Op.ListXattrs => new Nfs4ListXattrsResult(),
        Nfs4Op.RemoveXattr => new Nfs4RemoveXattrResult(),
        _ => new Nfs4StatusResult(op),
    };
}
