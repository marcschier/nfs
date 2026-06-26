using System.Buffers;
using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Nfs4ServerTests
{
    [Fact]
    public async Task GetRootHandle_ThenGetAttr_ReportsDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4Handle root = await nfs.GetRootHandleAsync(Token);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "getattr",
            [new Nfs4PutFhOp { Handle = root }, new Nfs4GetAttrOp { Request = TypeAndSize }],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4GetAttrResult attr = Assert.IsType<Nfs4GetAttrResult>(result.Operations[^1]);
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(attr.Attributes);
        Assert.Equal(Nfs4FileType.Directory, decoded.Type);
    }

    [Fact]
    public async Task Lookup_ThenGetAttr_OnAFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "readme.txt", Encoding.UTF8.GetBytes("hello world"));
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "lookup",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4LookupOp { Name = "readme.txt" },
                new Nfs4GetAttrOp { Request = TypeAndSize },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4GetAttrResult attr = Assert.IsType<Nfs4GetAttrResult>(result.Operations[^1]);
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(attr.Attributes);
        Assert.Equal(Nfs4FileType.Regular, decoded.Type);
        Assert.Equal(11ul, decoded.Size);
    }

    [Fact]
    public async Task Lookup_MissingName_StopsCompoundWithNoEntry()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "lookup",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "missing" }, new Nfs4GetFhOp()],
            Token);

        Assert.Equal(Nfs4Status.NoEntry, result.Status);
        // COMPOUND stops at the failing op: PUTROOTFH + failed LOOKUP, no GETFH.
        Assert.Equal(2, result.Operations.Count);
        Assert.Equal(Nfs4Status.NoEntry, result.Operations[^1].Status);
    }

    [Fact]
    public async Task LookupParent_FromChildDirectory_ReturnsParent()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory(fileSystem.Root, "sub");
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        Nfs4Handle root = await nfs.GetRootHandleAsync(Token);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "lookupp",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4LookupOp { Name = "sub" },
                new Nfs4LookupParentOp(),
                new Nfs4GetFhOp(),
                new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.FileId) },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Assert.Equal(root.Data, Assert.IsType<Nfs4GetFhResult>(result.Operations[3]).Handle.Data);
        Nfs4FileAttributes attributes = Nfs4FileAttributes.Decode(
            Assert.IsType<Nfs4GetAttrResult>(result.Operations[4]).Attributes);
        Assert.Equal(Nfs4FileType.Directory, attributes.Type);
        Assert.Equal(1ul, attributes.FileId);
    }

    [Fact]
    public async Task LookupParent_AtRoot_ReturnsNoEntry()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "lookupp-root",
            [new Nfs4PutRootFhOp(), new Nfs4LookupParentOp(), new Nfs4GetFhOp()],
            Token);

        Assert.Equal(Nfs4Status.NoEntry, result.Status);
        Assert.Equal(2, result.Operations.Count);
        Assert.Equal(Nfs4Status.NoEntry, result.Operations[^1].Status);
    }

    [Fact]
    public async Task SecInfo_ForChild_ReturnsAuthNoneAndAuthSys()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "readme.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "secinfo",
            [new Nfs4PutRootFhOp(), new Nfs4SecInfoOp { Name = "readme.txt" }],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4SecInfoResult secInfo = Assert.IsType<Nfs4SecInfoResult>(result.Operations[^1]);
        Assert.Contains(secInfo.Flavors, static flavor => flavor.Flavor == Nfs4SecurityFlavor.None);
        Assert.Contains(secInfo.Flavors, static flavor => flavor.Flavor == Nfs4SecurityFlavor.Sys);
    }

    [Fact]
    public async Task SecInfo_WhenRpcSecGssEnabled_ReturnsKerberosTriples()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "readme.txt", [1]);
        var program = new NfsProgram(fileSystem);
        await using var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            program,
            new RpcSecGssServer(new LoopbackGssMechanism()));
        server.Start();
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "secinfo-gss",
            [new Nfs4PutRootFhOp(), new Nfs4SecInfoOp { Name = "readme.txt" }],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4SecInfoResult secInfo = Assert.IsType<Nfs4SecInfoResult>(result.Operations[^1]);
        Nfs4SecInfo[] gss = secInfo.Flavors
            .Where(static flavor => flavor.Flavor == Nfs4SecurityFlavor.RpcSecGss)
            .ToArray();

        Assert.Equal(
            [Nfs4RpcGssService.None, Nfs4RpcGssService.Integrity, Nfs4RpcGssService.Privacy],
            gss.Select(static flavor => flavor.RpcSecGss!.Value.Service).ToArray());
        Assert.All(gss, static flavor => Assert.Equal(
            [0x2A, 0x86, 0x48, 0x86, 0xF7, 0x12, 0x01, 0x02, 0x02],
            flavor.RpcSecGss!.Value.Oid));
    }

    [Fact]
    public async Task Write_ThenRead_RoundTripsThroughCompound()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "data.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };

        byte[] payload = Encoding.UTF8.GetBytes("the quick brown fox");
        Nfs4CompoundResult write = await nfs.CompoundAsync(
            "write",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4WriteOp { Offset = 0, Data = payload, Stable = 2 }],
            Token);
        Assert.Equal(Nfs4Status.Ok, write.Status);
        Assert.Equal((uint)payload.Length, Assert.IsType<Nfs4WriteResult>(write.Operations[^1]).Count);

        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "read",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4ReadOp { Offset = 0, Count = 1024 }],
            Token);
        Nfs4ReadResult readResult = Assert.IsType<Nfs4ReadResult>(read.Operations[^1]);
        Assert.True(readResult.Eof);
        Assert.Equal(payload, readResult.Data);
    }

    [Fact]
    public async Task Commit_AfterWrite_ReturnsMatchingVerifier_AndDirectoryFails()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "commit.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };

        Nfs4CompoundResult write = await nfs.CompoundAsync(
            "write-commit",
            [
                new Nfs4PutFhOp { Handle = wire },
                new Nfs4WriteOp { Offset = 0, Data = "stable"u8.ToArray(), Stable = 2 },
                new Nfs4CommitOp { Offset = 0, Count = 0 },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, write.Status);
        Nfs4WriteResult writeResult = Assert.IsType<Nfs4WriteResult>(write.Operations[1]);
        Nfs4CommitResult commitResult = Assert.IsType<Nfs4CommitResult>(write.Operations[2]);
        Assert.Equal(writeResult.Verifier, commitResult.Verifier);

        Nfs4CompoundResult directoryCommit = await nfs.CompoundAsync(
            "commit-dir",
            [new Nfs4PutRootFhOp(), new Nfs4CommitOp()],
            Token);

        Assert.Equal(Nfs4Status.IsDirectory, directoryCommit.Status);
    }

    [Fact]
    public async Task Link_UsesSavedFileHandleAsSource_AndCurrentFileHandleAsDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "original.txt", "linked-data"u8.ToArray());
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };

        Nfs4CompoundResult link = await nfs.CompoundAsync(
            "link",
            [
                new Nfs4PutFhOp { Handle = wire },
                new Nfs4SaveFhOp(),
                new Nfs4PutRootFhOp(),
                new Nfs4LinkOp { NewName = "hardname.txt" },
                new Nfs4LookupOp { Name = "hardname.txt" },
                new Nfs4GetFhOp(),
                new Nfs4ReadOp { Offset = 0, Count = 1024 },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, link.Status);
        Assert.IsType<Nfs4LinkResult>(link.Operations[3]);
        Assert.Equal(wire.Data, Assert.IsType<Nfs4GetFhResult>(link.Operations[5]).Handle.Data);
        Assert.Equal("linked-data"u8.ToArray(), Assert.IsType<Nfs4ReadResult>(link.Operations[6]).Data);
    }

    [Fact]
    public async Task OpenAttr_ReadDirListsExtendedAttributes_AndLookupReadsNamedAttribute()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "xattrs.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };
        byte[] value = "note-value"u8.ToArray();

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "openattr",
            [
                new Nfs4PutFhOp { Handle = wire },
                new Nfs4SetXattrOp { Name = "user.note", Value = value },
                new Nfs4OpenAttrOp { CreateDirectory = true },
                new Nfs4ReadDirOp
                {
                    Cookie = 0,
                    DirectoryCount = 8192,
                    MaxCount = 32768,
                    Request = Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.Size),
                },
                new Nfs4LookupOp { Name = "user.note" },
                new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.Size) },
                new Nfs4ReadOp { Offset = 0, Count = 1024 },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4ReadDirResult readDir = Assert.IsType<Nfs4ReadDirResult>(result.Operations[3]);
        Assert.Contains(readDir.Entries, static entry => entry.Name == "user.note");
        Nfs4FileAttributes attributes = Nfs4FileAttributes.Decode(
            Assert.IsType<Nfs4GetAttrResult>(result.Operations[5]).Attributes);
        Assert.Equal(Nfs4FileType.NamedAttribute, attributes.Type);
        Assert.Equal((ulong)value.Length, attributes.Size);
        Assert.Equal(value, Assert.IsType<Nfs4ReadResult>(result.Operations[6]).Data);
    }

    [Fact]
    public async Task Create_Directory_ThenLookup()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult create = await nfs.CompoundAsync(
            "mkdir",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4CreateOp { Type = Nfs4CreateType.Directory, Name = "sub", Attributes = EmptyAttributes },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, create.Status);

        Nfs4CompoundResult lookup = await nfs.CompoundAsync(
            "lookup",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "sub" }, new Nfs4GetAttrOp { Request = TypeAndSize }],
            Token);
        Nfs4GetAttrResult attr = Assert.IsType<Nfs4GetAttrResult>(lookup.Operations[^1]);
        Assert.Equal(Nfs4FileType.Directory, Nfs4FileAttributes.Decode(attr.Attributes).Type);
    }

    [Fact]
    public async Task ReadDir_ListsEntriesWithAttributes()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1]);
        fileSystem.CreateFile(fileSystem.Root, "b.txt", [2]);
        fileSystem.CreateDirectory(fileSystem.Root, "sub");
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "readdir",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4ReadDirOp { Cookie = 0, DirectoryCount = 8192, MaxCount = 32768, Request = TypeAndSize },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Nfs4ReadDirResult readDir = Assert.IsType<Nfs4ReadDirResult>(result.Operations[^1]);
        Assert.True(readDir.Eof);
        Assert.Equal(["a.txt", "b.txt", "sub"], readDir.Entries.Select(e => e.Name).ToArray());
        Nfs4FileAttributes first = Nfs4FileAttributes.Decode(readDir.Entries[0].Attributes);
        Assert.Equal(Nfs4FileType.Regular, first.Type);
    }

    [Fact]
    public async Task Remove_DeletesFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "f", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult remove = await nfs.CompoundAsync(
            "remove",
            [new Nfs4PutRootFhOp(), new Nfs4RemoveOp { Name = "f" }],
            Token);
        Assert.Equal(Nfs4Status.Ok, remove.Status);

        Nfs4CompoundResult lookup = await nfs.CompoundAsync(
            "lookup",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "f" }],
            Token);
        Assert.Equal(Nfs4Status.NoEntry, lookup.Status);
    }

    [Fact]
    public async Task Rename_MovesFileUsingSaveFh()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle source = fileSystem.CreateDirectory(fileSystem.Root, "src");
        NfsFileHandle destination = fileSystem.CreateDirectory(fileSystem.Root, "dst");
        fileSystem.CreateFile(source, "a.txt", [9]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult rename = await nfs.CompoundAsync(
            "rename",
            [
                new Nfs4PutFhOp { Handle = new Nfs4Handle { Data = source.ToArray() } },
                new Nfs4SaveFhOp(),
                new Nfs4PutFhOp { Handle = new Nfs4Handle { Data = destination.ToArray() } },
                new Nfs4RenameOp { OldName = "a.txt", NewName = "b.txt" },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, rename.Status);
        Assert.IsType<Nfs4RenameResult>(rename.Operations[^1]);
    }

    [Fact]
    public async Task SetAttr_ChangesMode()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };

        Nfs4FAttr mode = new Nfs4FileAttributes { Mode = 0x124 }.Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Mode));
        Nfs4CompoundResult set = await nfs.CompoundAsync(
            "setattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4SetAttrOp { Attributes = mode }],
            Token);
        Assert.Equal(Nfs4Status.Ok, set.Status);

        Nfs4CompoundResult get = await nfs.CompoundAsync(
            "getattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Mode) }],
            Token);
        Nfs4GetAttrResult attr = Assert.IsType<Nfs4GetAttrResult>(get.Operations[^1]);
        Assert.Equal(0x124u, Nfs4FileAttributes.Decode(attr.Attributes).Mode);
    }


    [Fact]
    public async Task SetAttrAcl_ThenGetAttrAcl_RoundTrips()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "acl.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "alice"),
            new(NfsAceType.Deny, NfsAceDescriptor.None, NfsAceAccessMask.WriteData, "alice"),
        ];
        Nfs4FAttr encodedAcl = new Nfs4FileAttributes { AccessControlList = acl }
            .Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Acl));

        Nfs4CompoundResult set = await nfs.CompoundAsync(
            "set-acl",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4SetAttrOp { Attributes = encodedAcl }],
            Token);
        Assert.Equal(Nfs4Status.Ok, set.Status);

        Nfs4CompoundResult get = await nfs.CompoundAsync(
            "get-acl",
            [
                new Nfs4PutFhOp { Handle = wire },
                new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Acl, Nfs4AttributeId.AclSupport) },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, get.Status);
        Nfs4GetAttrResult result = Assert.IsType<Nfs4GetAttrResult>(get.Operations[^1]);
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(result.Attributes);
        Assert.Equal(0x00000003u, decoded.AclSupport);
        Assert.Equal(acl, decoded.AccessControlList);
    }

    [Fact]
    public async Task XattrLifecycle_RoundTripsAndMissingGetFails()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "x.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };
        byte[] value = "hello"u8.ToArray();

        Nfs4CompoundResult set = await nfs.CompoundAsync(
            "setxattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4SetXattrOp { Name = "user.note", Value = value }],
            Token);
        Assert.Equal(Nfs4Status.Ok, set.Status);

        Nfs4CompoundResult get = await nfs.CompoundAsync(
            "getxattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4GetXattrOp { Name = "user.note" }],
            Token);
        Assert.Equal(value, Assert.IsType<Nfs4GetXattrResult>(get.Operations[^1]).Value);

        Nfs4CompoundResult list = await nfs.CompoundAsync(
            "listxattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4ListXattrsOp { Cookie = 0, MaxCount = 1024 }],
            Token);
        Assert.Contains("user.note", Assert.IsType<Nfs4ListXattrsResult>(list.Operations[^1]).Names);

        Nfs4CompoundResult remove = await nfs.CompoundAsync(
            "removexattr",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4RemoveXattrOp { Name = "user.note" }],
            Token);
        Assert.Equal(Nfs4Status.Ok, remove.Status);

        Nfs4CompoundResult missing = await nfs.CompoundAsync(
            "getxattr-missing",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4GetXattrOp { Name = "user.note" }],
            Token);
        Assert.Equal(Nfs4Status.NoExtendedAttribute, missing.Status);
    }

    [Fact]
    public async Task XattrOversizeValue_ReturnsXattrTooBig()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "x.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var wire = new Nfs4Handle { Data = file.ToArray() };
        byte[] value = new byte[65537];

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "setxattr-big",
            [new Nfs4PutFhOp { Handle = wire }, new Nfs4SetXattrOp { Name = "user.big", Value = value }],
            Token);

        Assert.Equal(Nfs4Status.ExtendedAttributeTooBig, result.Status);
    }

    [Fact]
    public async Task GetFh_WithNoCurrentHandle_ReturnsNoFileHandle()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync("getfh", [new Nfs4GetFhOp()], Token);

        Assert.Equal(Nfs4Status.NoFileHandle, result.Status);
    }

    [Fact]
    public async Task SymbolicLink_ThenReadLink()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult create = await nfs.CompoundAsync(
            "symlink",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4CreateOp
                {
                    Type = Nfs4CreateType.SymbolicLink,
                    Name = "link",
                    LinkTarget = "/target/path",
                    Attributes = EmptyAttributes,
                },
                new Nfs4ReadLinkOp(),
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, create.Status);
        Nfs4ReadLinkResult readLink = Assert.IsType<Nfs4ReadLinkResult>(create.Operations[^1]);
        Assert.Equal("/target/path", readLink.Target);
    }

    [Fact]
    public async Task StatefulOpenWriteClose_FullFlow()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        Nfs4Handle root = await nfs.GetRootHandleAsync(Token);

        // 1. Establish and confirm a client identifier.
        Nfs4CompoundResult setClientId = await nfs.CompoundAsync(
            "setclientid",
            [new Nfs4SetClientIdOp { Verifier = new byte[8], Id = "test-client"u8.ToArray() }],
            Token);
        Assert.Equal(Nfs4Status.Ok, setClientId.Status);
        var clientResult = Assert.IsType<Nfs4SetClientIdResult>(setClientId.Operations[0]);

        Nfs4CompoundResult confirm = await nfs.CompoundAsync(
            "confirm",
            [new Nfs4SetClientIdConfirmOp { ClientId = clientResult.ClientId, Confirm = clientResult.ConfirmVerifier }],
            Token);
        Assert.Equal(Nfs4Status.Ok, confirm.Status);

        await CompleteReclaimAsync(nfs);

        // 2. OPEN (create) the file; capture its handle and open stateid.
        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open",
            [
                new Nfs4PutFhOp { Handle = root },
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientResult.ClientId,
                    Owner = "owner-1"u8.ToArray(),
                    OpenType = Nfs4OpenType.Create,
                    CreateMode = Nfs4CreateMode.Unchecked,
                    CreateAttributes = new Nfs4FAttr { Mask = Nfs4Bitmap.Empty, Values = [] },
                    Name = "state.txt",
                },
                new Nfs4GetFhOp(),
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, open.Status);
        var openResult = Assert.IsType<Nfs4OpenResult>(open.Operations[1]);
        Nfs4Handle file = Assert.IsType<Nfs4GetFhResult>(open.Operations[2]).Handle;

        // 3. WRITE using the open stateid, then CLOSE.
        byte[] payload = "stateful write"u8.ToArray();
        Nfs4CompoundResult write = await nfs.CompoundAsync(
            "write-close",
            [
                new Nfs4PutFhOp { Handle = file },
                new Nfs4WriteOp { StateId = openResult.StateId, Offset = 0, Data = payload, Stable = 2 },
                new Nfs4CloseOp { Seqid = 2, OpenStateId = openResult.StateId },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, write.Status);
        Assert.Equal((uint)payload.Length, Assert.IsType<Nfs4WriteResult>(write.Operations[1]).Count);
        Assert.IsType<Nfs4StateIdResult>(write.Operations[2]);

        // 4. READ the file back.
        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "read",
            [new Nfs4PutFhOp { Handle = file }, new Nfs4ReadOp { Offset = 0, Count = 1024 }],
            Token);
        Assert.Equal(payload, Assert.IsType<Nfs4ReadResult>(read.Operations[1]).Data);
    }

    [Fact]
    public async Task OpenDowngrade_NarrowsShareAndRejectsNonSubset()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "downgrade.txt", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        ulong clientId = await EstablishClientAsync(nfs, "downgrade-client");

        Nfs4StateId openStateId = await OpenAsync(nfs, clientId, "downgrade-owner", "downgrade.txt");
        Nfs4CompoundResult downgrade = await nfs.CompoundAsync(
            "open-downgrade",
            [new Nfs4OpenDowngradeOp
            {
                OpenStateId = openStateId,
                Seqid = 2,
                ShareAccess = Nfs4ShareAccess.Read,
            }],
            Token);

        Assert.Equal(Nfs4Status.Ok, downgrade.Status);
        var downgraded = Assert.IsType<Nfs4StateIdResult>(downgrade.Operations[0]);
        Assert.Equal(openStateId.Sequence + 1, downgraded.StateId.Sequence);
        Assert.Equal(openStateId.Other, downgraded.StateId.Other);

        Nfs4CompoundResult write = await nfs.CompoundAsync(
            "write-after-downgrade",
            [
                new Nfs4PutFhOp { Handle = new Nfs4Handle { Data = backing.ToArray() } },
                new Nfs4WriteOp { StateId = downgraded.StateId, Data = [1] },
            ],
            Token);
        Assert.Equal(Nfs4Status.BadStateId, write.Status);

        Nfs4CompoundResult invalid = await nfs.CompoundAsync(
            "open-downgrade-invalid",
            [new Nfs4OpenDowngradeOp
            {
                OpenStateId = downgraded.StateId,
                Seqid = 3,
                ShareAccess = Nfs4ShareAccess.Both,
            }],
            Token);

        Assert.Equal(Nfs4Status.InvalidArgument, invalid.Status);
    }

    [Fact]
    public async Task VerifyAndNverify_CompareCurrentAttributes()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "verify.txt", "hello"u8.ToArray());
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult attrs = await nfs.CompoundAsync(
            "attrs-for-verify",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4LookupOp { Name = "verify.txt" },
                new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Size) },
            ],
            Token);
        Nfs4FAttr matching = Assert.IsType<Nfs4GetAttrResult>(attrs.Operations[2]).Attributes;
        Nfs4FAttr mismatched = new Nfs4FileAttributes { Size = 99 }.Encode(Nfs4Bitmap.Of(Nfs4AttributeId.Size));

        Nfs4CompoundResult verifyOk = await nfs.CompoundAsync(
            "verify-ok",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "verify.txt" }, new Nfs4VerifyOp { Attributes = matching }],
            Token);
        Assert.Equal(Nfs4Status.Ok, verifyOk.Status);

        Nfs4CompoundResult verifyNotSame = await nfs.CompoundAsync(
            "verify-not-same",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "verify.txt" }, new Nfs4VerifyOp { Attributes = mismatched }],
            Token);
        Assert.Equal(Nfs4Status.NotSame, verifyNotSame.Status);

        Nfs4CompoundResult nverifyOk = await nfs.CompoundAsync(
            "nverify-ok",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "verify.txt" }, new Nfs4NverifyOp { Attributes = mismatched }],
            Token);
        Assert.Equal(Nfs4Status.Ok, nverifyOk.Status);

        Nfs4CompoundResult nverifySame = await nfs.CompoundAsync(
            "nverify-same",
            [new Nfs4PutRootFhOp(), new Nfs4LookupOp { Name = "verify.txt" }, new Nfs4NverifyOp { Attributes = matching }],
            Token);
        Assert.Equal(Nfs4Status.Same, nverifySame.Status);
    }

    [Fact]
    public async Task ExclusiveCreate_IsIdempotentForSameVerifier()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        ulong clientId = await EstablishClientAsync(nfs, "exclusive-client");
        Nfs4Handle root = await nfs.GetRootHandleAsync(Token);

        byte[] verifier = [1, 2, 3, 4, 5, 6, 7, 8];
        Nfs4CompoundResult created = await ExclusiveOpenAsync(nfs, root, clientId, "owner-1", 1, "exclusive.txt", verifier);

        Assert.Equal(Nfs4Status.Ok, created.Status);
        Nfs4Handle firstHandle = Assert.IsType<Nfs4GetFhResult>(created.Operations[2]).Handle;

        Nfs4CompoundResult retry = await ExclusiveOpenAsync(nfs, root, clientId, "owner-1", 2, "exclusive.txt", verifier);

        Assert.Equal(Nfs4Status.Ok, retry.Status);
        Assert.Equal(firstHandle.Data, Assert.IsType<Nfs4GetFhResult>(retry.Operations[2]).Handle.Data);

        Nfs4CompoundResult different = await ExclusiveOpenAsync(
            nfs,
            root,
            clientId,
            "owner-1",
            3,
            "exclusive.txt",
            [8, 7, 6, 5, 4, 3, 2, 1]);

        Assert.Equal(Nfs4Status.AlreadyExists, different.Status);
    }

    [Fact]
    public async Task Open_WithoutConfirmedClient_FailsStaleClientId()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        Nfs4Handle root = await nfs.GetRootHandleAsync(Token);
        await CompleteReclaimAsync(nfs);

        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open",
            [
                new Nfs4PutFhOp { Handle = root },
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ClientId = 999, // never registered
                    Owner = "owner"u8.ToArray(),
                    OpenType = Nfs4OpenType.Create,
                    CreateAttributes = new Nfs4FAttr { Mask = Nfs4Bitmap.Empty, Values = [] },
                    Name = "x.txt",
                },
            ],
            Token);

        Assert.Equal(Nfs4Status.StaleClientId, open.Status);
    }

    [Fact]
    public async Task ByteRangeLock_ConflictsAcrossOwners_ThenReleases()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "locked.bin", new byte[64]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        ulong clientId = await EstablishClientAsync(nfs);

        // Owner 1 opens the file and locks bytes [0, 10) for writing.
        Nfs4StateId open1 = await OpenAsync(nfs, clientId, "open-owner-1", "locked.bin");
        Nfs4CompoundResult lock1 = await nfs.CompoundAsync(
            "lock1",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(clientId, "lock-owner-1", open1, 0, 10)],
            Token);
        Assert.Equal(Nfs4Status.Ok, lock1.Status);
        var lockStateId1 = Assert.IsType<Nfs4LockResult>(lock1.Operations[1]).StateId;

        // Owner 2 opens the same file and an overlapping write lock is denied.
        Nfs4StateId open2 = await OpenAsync(nfs, clientId, "open-owner-2", "locked.bin");
        Nfs4CompoundResult lock2 = await nfs.CompoundAsync(
            "lock2",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(clientId, "lock-owner-2", open2, 5, 10)],
            Token);
        Assert.Equal(Nfs4Status.LockDenied, lock2.Status);
        Nfs4LockDenied denied = Assert.IsType<Nfs4LockResult>(lock2.Operations[1]).Denied;
        Assert.Equal(0ul, denied.Offset);
        Assert.Equal(Nfs4LockType.Write, denied.LockType);

        // Owner 1 releases the lock; owner 2 can now take it.
        Nfs4CompoundResult unlock = await nfs.CompoundAsync(
            "unlock",
            [
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LockUnlockOp { LockType = Nfs4LockType.Write, Seqid = 2, LockStateId = lockStateId1, Offset = 0, Length = 10 },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, unlock.Status);

        Nfs4CompoundResult lock2Retry = await nfs.CompoundAsync(
            "lock2-retry",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(clientId, "lock-owner-2", open2, 5, 10)],
            Token);
        Assert.Equal(Nfs4Status.Ok, lock2Retry.Status);
    }

    [Fact]
    public async Task LockTest_ReportsAvailabilityAndConflict()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "f.bin", new byte[64]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        ulong clientId = await EstablishClientAsync(nfs);
        Nfs4StateId open = await OpenAsync(nfs, clientId, "owner", "f.bin");

        // A free range tests OK.
        Nfs4CompoundResult free = await nfs.CompoundAsync(
            "lockt-free",
            [
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LockTestOp { LockType = Nfs4LockType.Write, Offset = 0, Length = 8, Owner = new Nfs4LockOwner(clientId, "probe"u8.ToArray()) },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, free.Status);

        // After a lock by a different owner, the same range is denied.
        await nfs.CompoundAsync(
            "hold",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(clientId, "holder", open, 0, 8)],
            Token);
        Nfs4CompoundResult busy = await nfs.CompoundAsync(
            "lockt-busy",
            [
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LockTestOp { LockType = Nfs4LockType.Write, Offset = 4, Length = 8, Owner = new Nfs4LockOwner(clientId, "probe"u8.ToArray()) },
            ],
            Token);
        Assert.Equal(Nfs4Status.LockDenied, busy.Status);
    }

    [Fact]
    public async Task Open_DuringGrace_ReturnsGrace_UntilReclaimCompletes()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "grace.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        ulong clientId = await EstablishClientWithoutCompletingReclaimAsync(nfs, "grace-client");

        Nfs4CompoundResult denied = await nfs.CompoundAsync(
            "open-grace",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientId,
                    Owner = "owner"u8.ToArray(),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = "grace.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Grace, denied.Status);

        await CompleteReclaimAsync(nfs);
        Nfs4CompoundResult allowed = await nfs.CompoundAsync(
            "open-after-grace",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 2,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientId,
                    Owner = "owner"u8.ToArray(),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = "grace.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, allowed.Status);
    }

    [Fact]
    public async Task Restart_WithStableStorage_AllowsKnownClientReclaimOnlyDuringGrace()
    {
        var clock = new FakeTimeProvider();
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "recover.txt", [1]);
        var file = new Nfs4Handle { Data = backing.ToArray() };
        string storageDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestStableStorage",
            Guid.NewGuid().ToString("N"));

        try
        {
            await using (RpcServer server = StartServer(fileSystem, clock, new FileStableStorage(storageDirectory)))
            {
                Nfs4Client nfs = await ConnectAsync(server);
                ulong clientId = await EstablishClientAsync(nfs, "recover-client");
                _ = await OpenAsync(nfs, clientId, "owner-before-restart", "recover.txt");
            }

            await using (RpcServer server = StartServer(fileSystem, clock, new FileStableStorage(storageDirectory)))
            {
                Nfs4Client nfs = await ConnectAsync(server);
                ulong recoveredClientId = await EstablishClientWithoutCompletingReclaimAsync(nfs, "recover-client");
                ulong unknownClientId = await EstablishClientWithoutCompletingReclaimAsync(nfs, "unknown-client");

                Nfs4CompoundResult reclaimed = await ReclaimOpenAsync(
                    nfs,
                    file,
                    recoveredClientId,
                    "recovered-owner",
                    1);
                Assert.Equal(Nfs4Status.Ok, reclaimed.Status);

                Nfs4CompoundResult unknown = await ReclaimOpenAsync(
                    nfs,
                    file,
                    unknownClientId,
                    "unknown-owner",
                    1);
                Assert.Equal(Nfs4Status.ReclaimBad, unknown.Status);

                Nfs4CompoundResult nonReclaim = await nfs.CompoundAsync(
                    "open-during-restart-grace",
                    [
                        new Nfs4PutRootFhOp(),
                        new Nfs4OpenOp
                        {
                            Seqid = 1,
                            ShareAccess = Nfs4ShareAccess.Read,
                            ClientId = unknownClientId,
                            Owner = "non-reclaim-owner"u8.ToArray(),
                            OpenType = Nfs4OpenType.NoCreate,
                            Name = "recover.txt",
                        },
                    ],
                    Token);
                Assert.Equal(Nfs4Status.Grace, nonReclaim.Status);

                await CompleteReclaimAsync(nfs);
                Nfs4CompoundResult afterGrace = await ReclaimOpenAsync(
                    nfs,
                    file,
                    recoveredClientId,
                    "too-late-owner",
                    2);
                Assert.Equal(Nfs4Status.NoGrace, afterGrace.Status);
            }
        }
        finally
        {
            if (Directory.Exists(storageDirectory))
            {
                Directory.Delete(storageDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LeaseExpiry_DropsOpenAndLockState()
    {
        var clock = new FakeTimeProvider();
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "leased.bin", new byte[32]);
        await using var server = StartServer(fileSystem, clock);
        Nfs4Client nfs = await ConnectAsync(server);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        ulong clientId = await EstablishClientAsync(nfs, "lease-client");
        Nfs4StateId open = await OpenAsync(nfs, clientId, "open-owner", "leased.bin");
        Nfs4CompoundResult locked = await nfs.CompoundAsync(
            "lock-lease",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(clientId, "lock-owner", open, 0, 8)],
            Token);
        Assert.Equal(Nfs4Status.Ok, locked.Status);

        clock.Advance(TimeSpan.FromSeconds(LeaseSeconds + 1));
        Nfs4CompoundResult close = await nfs.CompoundAsync(
            "close-expired",
            [new Nfs4CloseOp { Seqid = 2, OpenStateId = open }],
            Token);
        Assert.Equal(Nfs4Status.BadHandle, close.Status);

        ulong nextClientId = await EstablishClientAsync(nfs, "lease-client-2");
        Nfs4StateId nextOpen = await OpenAsync(nfs, nextClientId, "open-owner-2", "leased.bin");
        Nfs4CompoundResult relock = await nfs.CompoundAsync(
            "lock-after-expiry",
            [new Nfs4PutFhOp { Handle = file }, NewWriteLock(nextClientId, "lock-owner-2", nextOpen, 0, 8)],
            Token);
        Assert.Equal(Nfs4Status.Ok, relock.Status);
    }

    [Fact]
    public async Task Renew_ExtendsLease()
    {
        var clock = new FakeTimeProvider();
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "renew.bin", [1]);
        await using var server = StartServer(fileSystem, clock);
        Nfs4Client nfs = await ConnectAsync(server);

        ulong clientId = await EstablishClientAsync(nfs, "renew-client");
        Nfs4StateId open = await OpenAsync(nfs, clientId, "owner", "renew.bin");
        clock.Advance(TimeSpan.FromSeconds(LeaseSeconds - 5));
        Nfs4CompoundResult renew = await nfs.CompoundAsync("renew", [new Nfs4RenewOp { ClientId = clientId }], Token);
        Assert.Equal(Nfs4Status.Ok, renew.Status);

        clock.Advance(TimeSpan.FromSeconds(10));
        Nfs4CompoundResult close = await nfs.CompoundAsync(
            "close-renewed",
            [new Nfs4CloseOp { Seqid = 2, OpenStateId = open }],
            Token);
        Assert.Equal(Nfs4Status.Ok, close.Status);
    }

    [Fact]
    public async Task Open_ReadDelegation_CanBeReturned()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "delegated.txt", [1]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        ulong clientId = await EstablishClientAsync(nfs, "deleg-client");

        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open-deleg",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientId,
                    Owner = "owner"u8.ToArray(),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = "delegated.txt",
                },
            ],
            Token);
        var openResult = Assert.IsType<Nfs4OpenResult>(open.Operations[1]);
        Assert.Equal(Nfs4OpenResult.DelegationRead, openResult.DelegationType);

        Nfs4CompoundResult returned = await nfs.CompoundAsync(
            "delegreturn",
            [new Nfs4DelegReturnOp { StateId = openResult.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, returned.Status);

        Nfs4CompoundResult duplicate = await nfs.CompoundAsync(
            "delegreturn-again",
            [new Nfs4DelegReturnOp { StateId = openResult.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.BadStateId, duplicate.Status);
    }

    [Fact]
    public async Task OpenWrite_RecallsOtherClientsReadDelegation_ThenRetrySucceeds()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "recall.txt", [1]);
        await using var server = StartServer(fileSystem);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start();
        Nfs4Client nfs = await ConnectAsync(server);

        ulong clientA = await EstablishClientAsync(nfs, "client-a", callback);
        Nfs4OpenResult openA = await OpenReadDelegationAsync(nfs, clientA, "owner-a", "recall.txt");
        Assert.Equal(Nfs4OpenResult.DelegationRead, openA.DelegationType);

        ulong clientB = await EstablishClientAsync(nfs, "client-b");
        Nfs4CompoundResult blocked = await OpenForWriteAsync(nfs, clientB, "owner-b", "recall.txt");
        Assert.Equal(Nfs4Status.Delay, blocked.Status);

        Nfs4CallbackRecallOp recall = await callback.WaitForRecallAsync(Token);
        Assert.Equal(openA.DelegationStateId.Other, recall.StateId.Other);

        Nfs4CompoundResult returned = await nfs.CompoundAsync(
            "delegreturn",
            [new Nfs4DelegReturnOp { StateId = openA.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, returned.Status);

        Nfs4CompoundResult retry = await OpenForWriteAsync(nfs, clientB, "owner-b", "recall.txt");
        Assert.Equal(Nfs4Status.Ok, retry.Status);
        Assert.IsType<Nfs4OpenResult>(retry.Operations[1]);
    }

    [Fact]
    public async Task OpenWrite_RecallTimeoutRevokesDelegation_ThenRetrySucceeds()
    {
        var clock = new FakeTimeProvider();
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "timeout.txt", [1]);
        await using var server = StartServer(fileSystem, clock);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start();
        Nfs4Client nfs = await ConnectAsync(server);

        ulong clientA = await EstablishClientAsync(nfs, "timeout-a", callback);
        Nfs4OpenResult openA = await OpenReadDelegationAsync(nfs, clientA, "owner-a", "timeout.txt");
        Assert.Equal(Nfs4OpenResult.DelegationRead, openA.DelegationType);

        ulong clientB = await EstablishClientAsync(nfs, "timeout-b");
        Nfs4CompoundResult blocked = await OpenForWriteAsync(nfs, clientB, "owner-b", "timeout.txt");
        Assert.Equal(Nfs4Status.Delay, blocked.Status);
        _ = await callback.WaitForRecallAsync(Token);

        clock.Advance(TimeSpan.FromSeconds(2));
        Nfs4CompoundResult retry = await OpenForWriteAsync(nfs, clientB, "owner-b", "timeout.txt");
        Assert.Equal(Nfs4Status.Ok, retry.Status);

        Nfs4CompoundResult staleDelegation = await nfs.CompoundAsync(
            "delegreturn-revoked",
            [new Nfs4DelegReturnOp { StateId = openA.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.BadStateId, staleDelegation.Status);
    }

    [Fact]
    public async Task OpenWrite_GrantsWriteDelegation_AndReadOpenRecallsIt()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "write-deleg.txt", [1]);
        await using var server = StartServer(fileSystem);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start();
        Nfs4Client nfs = await ConnectAsync(server);

        ulong clientA = await EstablishClientAsync(nfs, "write-deleg-a", callback);
        Nfs4CompoundResult openAResult = await OpenForWriteAsync(nfs, clientA, "owner-a", "write-deleg.txt");
        var openA = Assert.IsType<Nfs4OpenResult>(openAResult.Operations[1]);
        Assert.Equal(Nfs4OpenResult.DelegationWrite, openA.DelegationType);
        Assert.Equal(ulong.MaxValue, openA.DelegationSpaceLimit);

        ulong clientB = await EstablishClientAsync(nfs, "write-deleg-b");
        Nfs4CompoundResult blocked = await OpenForReadAsync(nfs, clientB, "owner-b", "write-deleg.txt");
        Assert.Equal(Nfs4Status.Delay, blocked.Status);

        Nfs4CallbackRecallOp recall = await callback.WaitForRecallAsync(Token);
        Assert.Equal(openA.DelegationStateId.Other, recall.StateId.Other);

        Nfs4CompoundResult returned = await nfs.CompoundAsync(
            "write-delegreturn",
            [new Nfs4DelegReturnOp { StateId = openA.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, returned.Status);

        Nfs4CompoundResult retry = await OpenForReadAsync(nfs, clientB, "owner-b", "write-deleg.txt");
        Assert.Equal(Nfs4Status.Ok, retry.Status);
    }

    private static Nfs4LockOp NewWriteLock(ulong clientId, string owner, Nfs4StateId openStateId, ulong offset, ulong length) => new()
    {
        LockType = Nfs4LockType.Write,
        NewLockOwner = true,
        OpenSeqid = 1,
        OpenStateId = openStateId,
        LockSeqid = 1,
        LockOwner = new Nfs4LockOwner(clientId, Encoding.UTF8.GetBytes(owner)),
        Offset = offset,
        Length = length,
    };

    private static async ValueTask<ulong> EstablishClientAsync(
        Nfs4Client nfs,
        string id = "lock-client",
        Nfs4CallbackHost? callback = null)
    {
        ulong clientId = await EstablishClientWithoutCompletingReclaimAsync(nfs, id, callback);
        await CompleteReclaimAsync(nfs);
        return clientId;
    }

    private static async ValueTask<ulong> EstablishClientWithoutCompletingReclaimAsync(
        Nfs4Client nfs,
        string id,
        Nfs4CallbackHost? callback = null)
    {
        Nfs4CompoundResult setClientId = await nfs.CompoundAsync(
            "setclientid",
            [NewSetClientId(id, callback)],
            Token);
        var result = (Nfs4SetClientIdResult)setClientId.Operations[0];
        await nfs.CompoundAsync(
            "confirm",
            [new Nfs4SetClientIdConfirmOp { ClientId = result.ClientId, Confirm = result.ConfirmVerifier }],
            Token);
        return result.ClientId;
    }

    private static async ValueTask CompleteReclaimAsync(Nfs4Client nfs)
    {
        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "reclaim-complete",
            [new Nfs4ReclaimCompleteOp()],
            Token);
        Assert.Equal(Nfs4Status.Ok, result.Status);
    }

    private static async ValueTask<Nfs4StateId> OpenAsync(
        Nfs4Client nfs, ulong clientId, string owner, string name)
    {
        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = name,
                },
            ],
            Token);
        return Assert.IsType<Nfs4OpenResult>(open.Operations[1]).StateId;
    }

    private static async ValueTask<Nfs4OpenResult> OpenReadDelegationAsync(
        Nfs4Client nfs,
        ulong clientId,
        string owner,
        string name)
    {
        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open-read",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = name,
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, open.Status);
        return Assert.IsType<Nfs4OpenResult>(open.Operations[1]);
    }

    private static async ValueTask<Nfs4CompoundResult> OpenForWriteAsync(
        Nfs4Client nfs,
        ulong clientId,
        string owner,
        string name) =>
        await nfs.CompoundAsync(
            "open-write",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Write,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = name,
                },
            ],
            Token);

    private static async ValueTask<Nfs4CompoundResult> OpenForReadAsync(
        Nfs4Client nfs,
        ulong clientId,
        string owner,
        string name) =>
        await nfs.CompoundAsync(
            "open-read",
            [
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = name,
                },
            ],
            Token);

    private static async ValueTask<Nfs4CompoundResult> ReclaimOpenAsync(
        Nfs4Client nfs,
        Nfs4Handle file,
        ulong clientId,
        string owner,
        uint seqid) =>
        await nfs.CompoundAsync(
            "reclaim-open",
            [
                new Nfs4PutFhOp { Handle = file },
                new Nfs4OpenOp
                {
                    Seqid = seqid,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Reclaim = true,
                },
            ],
            Token);

    private static async ValueTask<Nfs4CompoundResult> ExclusiveOpenAsync(
        Nfs4Client nfs,
        Nfs4Handle root,
        ulong clientId,
        string owner,
        uint seqid,
        string name,
        byte[] verifier) =>
        await nfs.CompoundAsync(
            "exclusive-open",
            [
                new Nfs4PutFhOp { Handle = root },
                new Nfs4OpenOp
                {
                    Seqid = seqid,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.Create,
                    CreateMode = Nfs4CreateMode.Exclusive,
                    CreateVerifier = verifier,
                    Name = name,
                },
                new Nfs4GetFhOp(),
            ],
            Token);

    private static Nfs4SetClientIdOp NewSetClientId(string id, Nfs4CallbackHost? callback)
    {
        var op = new Nfs4SetClientIdOp
        {
            Verifier = new byte[8],
            Id = Encoding.UTF8.GetBytes(id),
        };
        if (callback is not null)
        {
            op.CallbackProgram = callback.Program;
            op.CallbackNetId = "tcp";
            op.CallbackAddress = UniversalAddress(callback.EndPoint);
            op.CallbackIdent = callback.CallbackIdent;
        }

        return op;
    }

    private static string UniversalAddress(IPEndPoint endPoint)
    {
        byte[] address = endPoint.Address.GetAddressBytes();
        int port = endPoint.Port;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{address[0]}.{address[1]}.{address[2]}.{address[3]}.{port >> 8}.{port & 0xFF}");
    }

    private static Nfs4Bitmap TypeAndSize => Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.Size);

    private static Nfs4FAttr EmptyAttributes => new() { Mask = Nfs4Bitmap.Empty, Values = [] };

    private const int LeaseSeconds = 90;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(
        INfsFileSystem fileSystem,
        TimeProvider? timeProvider = null,
        IStableStorage? stableStorage = null)
    {
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new NfsProgram(fileSystem, timeProvider, stableStorage: stableStorage));
        server.Start();
        return server;
    }

    private static async ValueTask<Nfs4Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nfs4Client(rpc);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _utcNow.UtcTicks;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class Nfs4CallbackHost : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<Nfs4CallbackRecallOp> _recalls = new();
        private readonly Queue<TaskCompletionSource<Nfs4CallbackRecallOp>> _waiters = new();
        private readonly RpcServer _server;

        private Nfs4CallbackHost(RpcServer server, uint program, uint callbackIdent)
        {
            _server = server;
            Program = program;
            CallbackIdent = callbackIdent;
        }

        public uint Program { get; }

        public uint CallbackIdent { get; }

        public IPEndPoint EndPoint => _server.LocalEndPoint;

        public static Nfs4CallbackHost Start()
        {
            Nfs4CallbackHost? host = null;
            var server = new RpcServer(
                new IPEndPoint(IPAddress.Loopback, 0),
            new CallbackProgram(CallbackProgramNumber, recall => host!.OnRecall(recall)));
            host = new Nfs4CallbackHost(server, CallbackProgramNumber, CallbackIdentNumber);
            server.Start();
            return host;
        }

        public ValueTask<Nfs4CallbackRecallOp> WaitForRecallAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_recalls.TryDequeue(out Nfs4CallbackRecallOp? recall))
                {
                    return ValueTask.FromResult(recall);
                }

                var waiter = new TaskCompletionSource<Nfs4CallbackRecallOp>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
                }

                _waiters.Enqueue(waiter);
                return new ValueTask<Nfs4CallbackRecallOp>(waiter.Task);
            }
        }

        public ValueTask DisposeAsync() => _server.DisposeAsync();

        private void OnRecall(Nfs4CallbackRecallOp recall)
        {
            lock (_gate)
            {
                if (_waiters.TryDequeue(out TaskCompletionSource<Nfs4CallbackRecallOp>? waiter))
                {
                    waiter.TrySetResult(recall);
                }
                else
                {
                    _recalls.Enqueue(recall);
                }
            }
        }

        private const uint CallbackProgramNumber = 0x40000001;

        private const uint CallbackIdentNumber = 42;

        private sealed class CallbackProgram(
            uint program,
            Action<Nfs4CallbackRecallOp> onRecall) : IRpcProgram
        {
            public uint Program => program;

            public ValueTask<RpcReplyPayload> InvokeAsync(
                RpcCallInfo request,
                ReadOnlyMemory<byte> arguments,
                CancellationToken cancellationToken)
            {
                if (request.Version != Nfs4.ProtocolVersion)
                {
                    return new ValueTask<RpcReplyPayload>(
                        RpcReplyPayload.ProgramMismatch(Nfs4.ProtocolVersion, Nfs4.ProtocolVersion));
                }

                if ((Nfs4CallbackProcedure)request.Procedure == Nfs4CallbackProcedure.Null)
                {
                    return new ValueTask<RpcReplyPayload>(
                        RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty));
                }

                if ((Nfs4CallbackProcedure)request.Procedure != Nfs4CallbackProcedure.Compound)
                {
                    return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProcedureUnavailable());
                }

                Nfs4CallbackCompoundArgs args = Decode<Nfs4CallbackCompoundArgs>(arguments);
                foreach (Nfs4CallbackArgOp operation in args.Operations)
                {
                    if (operation is Nfs4CallbackRecallOp recall)
                    {
                        onRecall(recall);
                    }
                }

                var result = new Nfs4CallbackCompoundResult
                {
                    Status = Nfs4Status.Ok,
                    Tag = args.Tag,
                };
                foreach (Nfs4CallbackArgOp _ in args.Operations)
                {
                    result.OperationStatuses.Add(Nfs4Status.Ok);
                }

                return new ValueTask<RpcReplyPayload>(Encode(result));
            }

            private static T Decode<T>(ReadOnlyMemory<byte> arguments)
                where T : IXdrSerializable<T>
            {
                var reader = new XdrReader(arguments.Span);
                return T.ReadFrom(ref reader);
            }

            private static RpcReplyPayload Encode<T>(T result)
                where T : IXdrSerializable<T>
            {
                var buffer = new ArrayBufferWriter<byte>();
                var writer = new XdrWriter(buffer);
                result.WriteTo(ref writer);
                return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
            }
        }
    }
}
