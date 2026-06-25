# Using the client

The library ships a typed client for each protocol version: `Nfs2Client`, `Nfs3Client`, and `Nfs4Client`. All three wrap a connected `RpcClient` and expose `async`/`ValueTask` methods. They are low-level: each method maps to one protocol procedure (or, for v4, one COMPOUND request), and results are returned as the protocol's own result types so that no compliance detail is hidden.

For everyday file access there is also a high-level `NfsClient` that hides handles and procedures behind path-based operations — start there if you do not need full protocol control.

## High-level client

`NfsClient` mounts an export and exposes ordinary file operations addressed by slash-separated paths relative to the export root. It is a convenience layer over `Nfs3Client` and is the quickest way to read and write files.

```csharp
using System.Net;
using Nfs.Client;

await using NfsClient client = await NfsClient.ConnectAsync(
    new IPEndPoint(IPAddress.Parse("192.0.2.10"), 2049), "/export/data");

await client.WriteAllBytesAsync("docs/greeting.txt", "hello"u8.ToArray());
byte[] bytes = await client.ReadAllBytesAsync("docs/greeting.txt");

foreach (string name in await client.ListAsync("docs"))
{
    Console.WriteLine(name);
}

NfsFileAttributes stat = await client.StatAsync("docs/greeting.txt");
Console.WriteLine($"{stat.Type} {stat.Size} bytes");

await client.CreateDirectoryAsync("docs/archive");
await client.DeleteAsync("docs/greeting.txt");
```

`ConnectAsync` expects the server to host both the NFS program (100003) and the MOUNT program (100005) on the given endpoint; failures throw `NfsException` carrying the `NfsStatus`. The rest of this guide covers the low-level per-version clients.

## Connecting

Every client is built on an `RpcClient` connected to the server's `IPEndPoint`. In practice you first obtain the export's root handle from the MOUNT service (NFS v2/v3) or via `PUTROOTFH` (NFS v4).

```csharp
using System.Net;
using Nfs.Client;
using Nfs.Mount;
using Nfs.Protocol.V3;
using Nfs.Rpc;

// Resolve the NFS port (usually 2049) via rpcbind if you do not know it.
var serverEndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 2049);

// 1. Obtain the export root handle from MOUNT (program 100005).
RpcClient mountRpc = await RpcClient.ConnectAsync(new IPEndPoint(serverEndPoint.Address, 635));
var mount = new Mount3Client(mountRpc);
Mount3MountResult mounted = await mount.MountAsync("/export/data");
Nfs3Handle root = new() { Data = mounted.Ok.Handle };

// 2. Talk NFSv3 to the data port.
RpcClient nfsRpc = await RpcClient.ConnectAsync(serverEndPoint);
var nfs = new Nfs3Client(nfsRpc);
```

`RpcClient.ConnectAsync` accepts a `CancellationToken`; all client methods accept one too.

## NFS version 3

`Nfs3Client` implements every NFS v3 procedure. Each returns a status-discriminated result type with an `IsSuccess` flag, a `Status`, and (on success) an `Ok` payload.

```csharp
// Look up a file and read it.
Nfs3LookupResult lookup = await nfs.LookupAsync(root, "readme.txt");
if (!lookup.IsSuccess)
{
    throw new InvalidOperationException($"lookup failed: {lookup.Status}");
}

Nfs3Handle file = lookup.Ok.Handle;
Nfs3ReadResult read = await nfs.ReadAsync(file, offset: 0, count: 64 * 1024);
ReadOnlyMemory<byte> bytes = read.Ok.Data;

// Create and write a new file.
Nfs3CreateResult create = await nfs.CreateAsync(root, "greeting.txt", Nfs3SetAttributes.None);
Nfs3Handle created = create.Ok.Handle!.Value;
await nfs.WriteAsync(created, offset: 0, "hello"u8.ToArray(), Nfs3StableHow.FileSync);

// Enumerate a directory.
Nfs3ReadDirResult dir = await nfs.ReadDirectoryAsync(root);
foreach (Nfs3DirEntry entry in dir.Ok.Entries)
{
    Console.WriteLine(entry.Name);
}
```

The full set of methods is: `NullAsync`, `GetAttributesAsync`, `SetAttributesAsync`, `LookupAsync`, `AccessAsync`, `ReadLinkAsync`/`ReadSymbolicLinkAsync`, `ReadAsync`, `WriteAsync`, `CreateAsync`, `MakeDirectoryAsync`, `CreateSymbolicLinkAsync`, `RemoveAsync`, `RemoveDirectoryAsync`, `RenameAsync`, `LinkAsync`, `ReadDirectoryAsync`, `ReadDirectoryPlusAsync`, `FileSystemStatusAsync`, `FileSystemInfoAsync`, `PathConfigurationAsync`, and `CommitAsync`.

## NFS version 2

`Nfs2Client` mirrors the v3 client for the older protocol. The notable differences come from the wire format: handles are a fixed 32 bytes, file sizes and offsets are 32-bit, and the maximum transfer size is 8 KiB.

```csharp
var nfs2 = new Nfs2Client(rpc);
Nfs2DirOpResult lookup = await nfs2.LookupAsync(root2, "readme.txt");
Nfs2ReadResult read = await nfs2.ReadAsync(lookup.Handle, offset: 0, count: 8192);
```

## NFS version 4.0

NFS v4 has a single RPC procedure, `COMPOUND`, which carries an ordered array of operations that share a current and a saved file handle. `Nfs4Client` exposes `CompoundAsync(tag, ops...)` plus a `GetRootHandleAsync` convenience that issues `PUTROOTFH` + `GETFH`.

Attributes are requested with a bitmap and returned as an encoded `fattr4`, which you decode with `Nfs4FileAttributes.Decode`.

```csharp
using Nfs.Protocol.V4;

var nfs4 = new Nfs4Client(rpc);
Nfs4Handle root = await nfs4.GetRootHandleAsync();

// Look up a file and read its type and size in one round trip.
Nfs4Bitmap want = Nfs4Bitmap.Of(Nfs4AttributeId.Type, Nfs4AttributeId.Size);
Nfs4CompoundResult result = await nfs4.CompoundAsync(
    "open-read",
    new Nfs4PutFhOp { Handle = root },
    new Nfs4LookupOp { Name = "readme.txt" },
    new Nfs4GetAttrOp { Request = want },
    new Nfs4ReadOp { Offset = 0, Count = 64 * 1024 });

if (result.Status == Nfs4Status.Ok)
{
    var attr = (Nfs4GetAttrResult)result.Operations[2];
    Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(attr.Attributes);
    Console.WriteLine($"type={decoded.Type} size={decoded.Size}");

    var read = (Nfs4ReadResult)result.Operations[3];
    Console.WriteLine($"read {read.Data.Length} bytes, eof={read.Eof}");
}
```

A COMPOUND stops executing at the first operation that fails, and `result.Status` is that operation's status. The reply's `Operations` list therefore contains one result per operation that actually ran.

> The v4 client and server implement the COMPOUND operation set including SETCLIENTID/OPEN/CLOSE, byte-range LOCK/LOCKT/LOCKU, READ and WRITE delegations, and v4.1 sessions. Stateless reads and writes may use the anonymous (all-zero) stateid; stateful reads and writes use the stateid returned by OPEN. The unified `NfsClient.ConnectNegotiatedAsync` selects the highest version a server supports. See the [feature matrix](../feature-matrix.md) for the exact status.

## Authentication

By default the clients send `AUTH_NONE`. To present a Unix identity, pass an `AUTH_SYS` credential when constructing the client:

```csharp
OpaqueAuth cred = AuthSys.Create(uid: 1000, gid: 1000, machineName: "host");
var nfs = new Nfs3Client(rpc, cred);
```
