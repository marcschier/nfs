using Nfs.Abstractions;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Protocol.V4.Tests;

public sealed class Nfs4WireTests
{
    private const uint Nfs4MappingAclSupport = 0x00000001 | 0x00000002;

    [Fact]
    public void Bitmap_SetsAndTestsBits()
    {
        Nfs4Bitmap bitmap = Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.Mode, Nfs4AttributeId.TimeModify);

        Assert.True(bitmap.IsSet(Nfs4AttributeId.Type));
        Assert.True(bitmap.IsSet(Nfs4AttributeId.Mode));
        Assert.True(bitmap.IsSet(Nfs4AttributeId.TimeModify));
        Assert.False(bitmap.IsSet(Nfs4AttributeId.Size));
    }

    [Fact]
    public void Bitmap_RoundTrips()
    {
        Nfs4Bitmap bitmap = Nfs4Bitmap.Of(Nfs4AttributeId.Size, Nfs4AttributeId.Owner);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        bitmap.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4Bitmap decoded = Nfs4Bitmap.ReadFrom(ref reader);

        Assert.Equal(bitmap, decoded);
    }

    [Fact]
    public void FileAttributes_EncodeDecode_RoundTripsRequestedSubset()
    {
        var attributes = new Nfs4FileAttributes
        {
            Type = Nfs4FileType.Regular,
            Change = 0x1122334455667788,
            Size = 4096,
            FileId = 42,
            Mode = 0x1A4,
            NumLinks = 1,
            Owner = "1000",
            OwnerGroup = "1000",
            TimeModify = new Nfs4Time { Seconds = 1700000000, Nanoseconds = 500 },
            // Present but not requested: must be omitted.
            SpaceUsed = 99999,
        };

        Nfs4Bitmap requested = Nfs4Bitmap.Of(
            Nfs4AttributeId.Type,
            Nfs4AttributeId.Size,
            Nfs4AttributeId.FileId,
            Nfs4AttributeId.Mode,
            Nfs4AttributeId.Owner,
            Nfs4AttributeId.TimeModify);

        Nfs4FAttr encoded = attributes.Encode(requested);
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(encoded);

        Assert.Equal(Nfs4FileType.Regular, decoded.Type);
        Assert.Equal(4096ul, decoded.Size);
        Assert.Equal(42ul, decoded.FileId);
        Assert.Equal(0x1A4u, decoded.Mode);
        Assert.Equal("1000", decoded.Owner);
        Assert.Equal(1700000000L, decoded.TimeModify!.Value.Seconds);
        Assert.Null(decoded.SpaceUsed); // not requested
        Assert.Null(decoded.Change);    // not requested
    }

    [Fact]
    public void FileAttributes_EncodeDecode_RoundTripsAclAndAclSupport()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "alice"),
            new(NfsAceType.Deny, NfsAceDescriptor.IdentifierGroup, NfsAceAccessMask.WriteData, "writers"),
        ];
        var attributes = new Nfs4FileAttributes
        {
            AccessControlList = acl,
            AclSupport = Nfs4MappingAclSupport,
        };

        Nfs4FAttr encoded = attributes.Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Acl, Nfs4AttributeId.AclSupport));
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(encoded);

        Assert.Equal(Nfs4MappingAclSupport, decoded.AclSupport);
        NfsAccessControlEntry[] decodedAcl = Assert.IsAssignableFrom<IReadOnlyList<NfsAccessControlEntry>>(
            decoded.AccessControlList).ToArray();
        Assert.Equal(acl, decodedAcl);
    }

    [Fact]
    public void CallbackCompound_RoundTripsSequenceAndRecall()
    {
        var args = new Nfs4CallbackCompoundArgs { Tag = "cb", MinorVersion = Nfs4.MinorVersion1 };
        byte[] sessionId = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        args.Operations.Add(new Nfs4CallbackSequenceOp
        {
            SessionId = sessionId,
            SequenceId = 7,
            SlotId = 0,
            HighestSlotId = 0,
            CacheThis = false,
        });
        args.Operations.Add(new Nfs4CallbackRecallOp
        {
            StateId = new Nfs4StateId { Sequence = 1, Other = [9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9] },
            Handle = new Nfs4Handle { Data = [4, 3, 2, 1] },
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CallbackCompoundArgs decoded = Nfs4CallbackCompoundArgs.ReadFrom(ref reader);

        Assert.Equal(Nfs4.MinorVersion1, decoded.MinorVersion);
        var sequence = Assert.IsType<Nfs4CallbackSequenceOp>(decoded.Operations[0]);
        Assert.Equal(sessionId, sequence.SessionId);
        Assert.Equal(7u, sequence.SequenceId);
        Assert.Equal([4, 3, 2, 1], Assert.IsType<Nfs4CallbackRecallOp>(decoded.Operations[1]).Handle.Data);
    }

    [Fact]
    public void CallbackCompound_RoundTripsNotifyLock()
    {
        var args = new Nfs4CallbackCompoundArgs { Tag = "notify-lock", MinorVersion = Nfs4.MinorVersion1 };
        args.Operations.Add(new Nfs4CallbackNotifyLockOp
        {
            Handle = new Nfs4Handle { Data = [1, 3, 5, 7] },
            Owner = new Nfs4LockOwner(42, "lock-owner"u8.ToArray()),
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CallbackCompoundArgs decoded = Nfs4CallbackCompoundArgs.ReadFrom(ref reader);

        Nfs4CallbackNotifyLockOp notifyLock = Assert.IsType<Nfs4CallbackNotifyLockOp>(decoded.Operations[0]);
        Assert.Equal([1, 3, 5, 7], notifyLock.Handle.Data);
        Assert.Equal(42ul, notifyLock.Owner.ClientId);
        Assert.Equal("lock-owner"u8.ToArray(), notifyLock.Owner.Owner);
    }

    [Fact]
    public void Nfs40CompletenessOperations_RoundTrip()
    {
        var stateId = new Nfs4StateId { Sequence = 7, Other = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] };
        Nfs4FAttr attributes = new Nfs4FileAttributes { Size = 123 }.Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Size));
        var args = new Nfs4CompoundArgs { Tag = "m27", MinorVersion = Nfs4.MinorVersion0 };
        args.Operations.Add(new Nfs4OpenDowngradeOp
        {
            OpenStateId = stateId,
            Seqid = 8,
            ShareAccess = Nfs4ShareAccess.Read,
        });
        args.Operations.Add(new Nfs4VerifyOp { Attributes = attributes });
        args.Operations.Add(new Nfs4NverifyOp { Attributes = attributes });
        args.Operations.Add(new Nfs4OpenOp
        {
            Seqid = 9,
            ShareAccess = Nfs4ShareAccess.Both,
            ClientId = 42,
            Owner = "owner"u8.ToArray(),
            OpenType = Nfs4OpenType.Create,
            CreateMode = Nfs4CreateMode.Exclusive,
            CreateVerifier = [8, 7, 6, 5, 4, 3, 2, 1],
            Name = "created.txt",
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundArgs decoded = Nfs4CompoundArgs.ReadFrom(ref reader);

        var downgrade = Assert.IsType<Nfs4OpenDowngradeOp>(decoded.Operations[0]);
        Assert.Equal(stateId.Other, downgrade.OpenStateId.Other);
        Assert.Equal(8u, downgrade.Seqid);
        Assert.Equal(Nfs4ShareAccess.Read, downgrade.ShareAccess);
        Assert.Equal(attributes.Values, Assert.IsType<Nfs4VerifyOp>(decoded.Operations[1]).Attributes.Values);
        Assert.Equal(attributes.Values, Assert.IsType<Nfs4NverifyOp>(decoded.Operations[2]).Attributes.Values);
        var open = Assert.IsType<Nfs4OpenOp>(decoded.Operations[3]);
        Assert.Equal(Nfs4CreateMode.Exclusive, open.CreateMode);
        Assert.Equal([8, 7, 6, 5, 4, 3, 2, 1], open.CreateVerifier);
    }

    [Fact]
    public void OpenDowngradeResult_RoundTrips()
    {
        var stateId = new Nfs4StateId { Sequence = 2, Other = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1] };
        var result = new Nfs4CompoundResult { Status = Nfs4Status.Ok, Tag = "downgrade-result" };
        result.Operations.Add(new Nfs4StateIdResult(Nfs4Op.OpenDowngrade)
        {
            Status = Nfs4Status.Ok,
            StateId = stateId,
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundResult decoded = Nfs4CompoundResult.ReadFrom(ref reader);

        var downgrade = Assert.IsType<Nfs4StateIdResult>(decoded.Operations[0]);
        Assert.Equal(Nfs4Op.OpenDowngrade, downgrade.Op);
        Assert.Equal(stateId.Other, downgrade.StateId.Other);
    }

    [Fact]
    public void CopyOffloadOperations_RoundTrip()
    {
        var stateId = new Nfs4StateId { Sequence = 9, Other = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] };
        var args = new Nfs4CompoundArgs { Tag = "copy-offload", MinorVersion = Nfs4.MinorVersion2 };
        args.Operations.Add(new Nfs4CopyNotifyOp
        {
            SourceStateId = stateId,
            Destination = new Nfs4NetLocation { NetId = "tcp", Address = "127.0.0.1.1.2" },
        });
        args.Operations.Add(new Nfs4OffloadStatusOp { StateId = stateId });
        args.Operations.Add(new Nfs4OffloadCancelOp { StateId = stateId });
        args.Operations.Add(new Nfs4CopyOp
        {
            SourceStateId = stateId,
            Count = 12,
            Synchronous = false,
            SourceServers = { new Nfs4NetLocation { NetId = "tcp", Address = "127.0.0.1.1.2" } },
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundArgs decoded = Nfs4CompoundArgs.Decode(ref reader);

        Assert.IsType<Nfs4CopyNotifyOp>(decoded.Operations[0]);
        Assert.Equal(stateId.Other, Assert.IsType<Nfs4OffloadStatusOp>(decoded.Operations[1]).StateId.Other);
        Assert.Equal(stateId.Other, Assert.IsType<Nfs4OffloadCancelOp>(decoded.Operations[2]).StateId.Other);
        Nfs4CopyOp copy = Assert.IsType<Nfs4CopyOp>(decoded.Operations[3]);
        Assert.False(copy.Synchronous);
        Assert.Single(copy.SourceServers);
    }

    [Fact]
    public void SessionStateManagementOperations_RoundTrip()
    {
        byte[] sessionId = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var stateId = new Nfs4StateId { Sequence = 3, Other = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1] };
        var args = new Nfs4CompoundArgs { Tag = "m31", MinorVersion = Nfs4.MinorVersion1 };
        args.Operations.Add(new Nfs4BackchannelCtlOp { CallbackProgram = 0x40000131 });
        args.Operations.Add(new Nfs4BindConnToSessionOp
        {
            SessionId = sessionId,
            Direction = Nfs4ChannelDirectionFromClient.BackOrBoth,
            UseConnectionInRdmaMode = true,
        });
        args.Operations.Add(new Nfs4FreeStateIdOp { StateId = stateId });
        var test = new Nfs4TestStateIdOp();
        test.StateIds.Add(stateId);
        test.StateIds.Add(new Nfs4StateId { Sequence = 1, Other = new byte[Nfs4.OtherSize] });
        args.Operations.Add(test);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundArgs decoded = Nfs4CompoundArgs.Decode(ref reader);

        Assert.Equal(0x40000131u, Assert.IsType<Nfs4BackchannelCtlOp>(decoded.Operations[0]).CallbackProgram);
        var bind = Assert.IsType<Nfs4BindConnToSessionOp>(decoded.Operations[1]);
        Assert.Equal(sessionId, bind.SessionId);
        Assert.Equal(Nfs4ChannelDirectionFromClient.BackOrBoth, bind.Direction);
        Assert.True(bind.UseConnectionInRdmaMode);
        Assert.Equal(stateId.Other, Assert.IsType<Nfs4FreeStateIdOp>(decoded.Operations[2]).StateId.Other);
        Assert.Equal(2, Assert.IsType<Nfs4TestStateIdOp>(decoded.Operations[3]).StateIds.Count);
    }

    [Fact]
    public void SessionStateManagementResults_RoundTrip()
    {
        byte[] sessionId = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var result = new Nfs4CompoundResult { Status = Nfs4Status.Ok, Tag = "m31-results" };
        result.Operations.Add(new Nfs4StatusResult(Nfs4Op.BackchannelCtl) { Status = Nfs4Status.Ok });
        result.Operations.Add(new Nfs4BindConnToSessionResult
        {
            Status = Nfs4Status.Ok,
            SessionId = sessionId,
            Direction = Nfs4ChannelDirectionFromServer.Both,
            UseConnectionInRdmaMode = true,
        });
        result.Operations.Add(new Nfs4StatusResult(Nfs4Op.FreeStateId) { Status = Nfs4Status.Ok });
        var test = new Nfs4TestStateIdResult { Status = Nfs4Status.Ok };
        test.StateStatuses.Add(Nfs4Status.Ok);
        test.StateStatuses.Add(Nfs4Status.BadStateId);
        result.Operations.Add(test);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundResult decoded = Nfs4CompoundResult.Decode(ref reader);

        Assert.Equal(Nfs4Op.BackchannelCtl, decoded.Operations[0].Op);
        var bind = Assert.IsType<Nfs4BindConnToSessionResult>(decoded.Operations[1]);
        Assert.Equal(sessionId, bind.SessionId);
        Assert.Equal(Nfs4ChannelDirectionFromServer.Both, bind.Direction);
        Assert.Equal(Nfs4Op.FreeStateId, decoded.Operations[2].Op);
        Assert.Equal([Nfs4Status.Ok, Nfs4Status.BadStateId], Assert.IsType<Nfs4TestStateIdResult>(decoded.Operations[3]).StateStatuses);
    }

    [Fact]
    public void PnfsMultiDataServerDeviceAndLayout_RoundTrip()
    {
        var address = new Nfs4FileLayoutDataServerAddress
        {
            StripeIndices = [0, 1, 2, 0, 1, 2],
            MultipathDataServers =
            [
                [new Nfs4NetAddress { NetId = "tcp", Uaddr = "127.0.0.1.8.1" }],
                [new Nfs4NetAddress { NetId = "tcp", Uaddr = "127.0.0.1.8.2" }],
                [new Nfs4NetAddress { NetId = "tcp", Uaddr = "127.0.0.1.8.3" }],
            ],
        };

        Nfs4FileLayoutDataServerAddress decodedAddress = Nfs4FileLayoutDataServerAddress.Decode(address.Encode());

        Assert.Equal([0u, 1, 2, 0, 1, 2], decodedAddress.StripeIndices);
        Assert.Equal(3, decodedAddress.MultipathDataServers.Length);
        Assert.Equal("127.0.0.1.8.2", decodedAddress.MultipathDataServers[1][0].Uaddr);

        var layout = new Nfs4FileLayout
        {
            DeviceId = Nfs4Pnfs.DefaultDeviceId.ToArray(),
            Util = Nfs4Pnfs.FileLayoutUtilDense,
            StripeUnit = 65536,
            FirstStripeIndex = 1,
            PatternOffset = 4096,
            FileHandles =
            [
                new Nfs4Handle { Data = [1, 1] },
                new Nfs4Handle { Data = [2, 2] },
                new Nfs4Handle { Data = [3, 3] },
            ],
        };

        Nfs4FileLayout decodedLayout = Nfs4FileLayout.Decode(layout.Encode());

        Assert.Equal(Nfs4Pnfs.DefaultDeviceId, decodedLayout.DeviceId);
        Assert.Equal(65536u, decodedLayout.StripeUnit);
        Assert.Equal(Nfs4Pnfs.FileLayoutUtilDense, decodedLayout.Util & Nfs4Pnfs.FileLayoutUtilFlagMask);
        Assert.Equal(1u, decodedLayout.FirstStripeIndex);
        Assert.Equal(4096ul, decodedLayout.PatternOffset);
        Assert.Equal([3, 3], decodedLayout.FileHandles[2].Data);
    }

    [Fact]
    public void CopyOffloadResultsAndCallback_RoundTrip()
    {
        var stateId = new Nfs4StateId { Sequence = 1, Other = [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1] };
        var result = new Nfs4CompoundResult { Status = Nfs4Status.Ok, Tag = "copy-offload" };
        var notify = new Nfs4CopyNotifyResult
        {
            Status = Nfs4Status.Ok,
            LeaseTime = new Nfs4Time { Seconds = 90 },
            StateId = stateId,
        };
        notify.SourceLocations.Add(new Nfs4NetLocation { NetId = "tcp", Address = "127.0.0.1.1.2" });
        result.Operations.Add(notify);
        result.Operations.Add(new Nfs4OffloadStatusResult
        {
            Status = Nfs4Status.Ok,
            Count = 7,
            Complete = true,
            CompleteStatus = Nfs4Status.Ok,
        });
        result.Operations.Add(new Nfs4StatusResult(Nfs4Op.OffloadCancel) { Status = Nfs4Status.Ok });
        result.Operations.Add(new Nfs4CopyResult
        {
            Status = Nfs4Status.Ok,
            Response = new Nfs4CopyWriteResponse { CallbackId = stateId, Verifier = [1, 2, 3, 4, 5, 6, 7, 8] },
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundResult decoded = Nfs4CompoundResult.Decode(ref reader);

        Assert.Equal(stateId.Other, Assert.IsType<Nfs4CopyNotifyResult>(decoded.Operations[0]).StateId.Other);
        Assert.True(Assert.IsType<Nfs4OffloadStatusResult>(decoded.Operations[1]).Complete);
        Assert.Equal(stateId.Other, Assert.IsType<Nfs4CopyResult>(decoded.Operations[3]).Response.CallbackId!.Value.Other);

        var callback = new Nfs4CallbackCompoundArgs { Tag = "offload", MinorVersion = Nfs4.MinorVersion1 };
        callback.Operations.Add(new Nfs4CallbackOffloadOp
        {
            StateId = stateId,
            Status = Nfs4Status.Ok,
            Response = new Nfs4CopyWriteResponse { Count = 7, Verifier = [8, 7, 6, 5, 4, 3, 2, 1] },
        });

        buffer = new System.Buffers.ArrayBufferWriter<byte>();
        writer = new XdrWriter(buffer);
        callback.WriteTo(ref writer);
        reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CallbackOffloadOp offload = Assert.IsType<Nfs4CallbackOffloadOp>(
            Nfs4CallbackCompoundArgs.ReadFrom(ref reader).Operations[0]);
        Assert.Equal(7ul, offload.Response.Count);
    }

    [Fact]
    public void Compound_RoundTripsXattrOperationsAndResults()
    {
        var args = new Nfs4CompoundArgs { Tag = "x", MinorVersion = Nfs4.MinorVersion2 };
        args.Operations.Add(new Nfs4GetXattrOp { Name = "user.mime" });
        args.Operations.Add(new Nfs4SetXattrOp { Name = "user.mime", Value = "text/plain"u8.ToArray() });
        args.Operations.Add(new Nfs4ListXattrsOp { Cookie = 0, MaxCount = 1024 });
        args.Operations.Add(new Nfs4RemoveXattrOp { Name = "user.mime" });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundArgs decoded = Nfs4CompoundArgs.Decode(ref reader);

        Assert.IsType<Nfs4GetXattrOp>(decoded.Operations[0]);
        Assert.Equal("user.mime", Assert.IsType<Nfs4SetXattrOp>(decoded.Operations[1]).Name);
        Assert.IsType<Nfs4ListXattrsOp>(decoded.Operations[2]);
        Assert.IsType<Nfs4RemoveXattrOp>(decoded.Operations[3]);
    }

    [Fact]
    public void Compound_RoundTripsOperations()
    {
        var args = new Nfs4CompoundArgs { Tag = "test", MinorVersion = 0 };
        args.Operations.Add(new Nfs4PutRootFhOp());
        args.Operations.Add(new Nfs4LookupOp { Name = "etc" });
        args.Operations.Add(new Nfs4SecInfoOp { Name = "krb5.conf" });
        args.Operations.Add(new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Size) });
        args.Operations.Add(new Nfs4GetFhOp());

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundArgs decoded = Nfs4CompoundArgs.Decode(ref reader);

        Assert.Equal("test", decoded.Tag);
        Assert.Equal(5, decoded.Operations.Count);
        Assert.Equal(Nfs4Op.PutRootFh, decoded.Operations[0].Op);
        Assert.Equal("etc", Assert.IsType<Nfs4LookupOp>(decoded.Operations[1]).Name);
        Assert.Equal("krb5.conf", Assert.IsType<Nfs4SecInfoOp>(decoded.Operations[2]).Name);
        Assert.Equal(Nfs4Op.GetFh, decoded.Operations[4].Op);
    }

    [Fact]
    public void SecInfoResult_RoundTripsFlavors()
    {
        var result = new Nfs4CompoundResult { Status = Nfs4Status.Ok, Tag = "secinfo" };
        result.Operations.Add(new Nfs4SecInfoResult(Nfs4Op.SecInfo)
        {
            Status = Nfs4Status.Ok,
            Flavors =
            [
                Nfs4SecInfo.AuthNone,
                Nfs4SecInfo.AuthSys,
                Nfs4SecInfo.RpcGss(new Nfs4RpcSecGssInfo(
                    [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x12, 0x01, 0x02, 0x02],
                    0,
                    Nfs4RpcGssService.Integrity)),
            ],
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundResult decoded = Nfs4CompoundResult.Decode(ref reader);

        Nfs4SecInfoResult secInfo = Assert.IsType<Nfs4SecInfoResult>(decoded.Operations[0]);
        Assert.Equal(
            [Nfs4SecurityFlavor.None, Nfs4SecurityFlavor.Sys, Nfs4SecurityFlavor.RpcSecGss],
            secInfo.Flavors.Select(static flavor => flavor.Flavor).ToArray());
        Assert.Equal(Nfs4RpcGssService.Integrity, secInfo.Flavors[2].RpcSecGss!.Value.Service);
    }

    [Fact]
    public void CompoundResult_RoundTripsResults()
    {
        var result = new Nfs4CompoundResult { Status = Nfs4Status.Ok, Tag = "t" };
        result.Operations.Add(new Nfs4StatusResult(Nfs4Op.PutRootFh) { Status = Nfs4Status.Ok });
        result.Operations.Add(new Nfs4GetFhResult
        {
            Status = Nfs4Status.Ok,
            Handle = new Nfs4Handle { Data = [1, 2, 3, 4] },
        });

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.Encode(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs4CompoundResult decoded = Nfs4CompoundResult.Decode(ref reader);

        Assert.Equal(Nfs4Status.Ok, decoded.Status);
        Assert.Equal(2, decoded.Operations.Count);
        Nfs4GetFhResult getFh = Assert.IsType<Nfs4GetFhResult>(decoded.Operations[1]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, getFh.Handle.Data);
    }
}
