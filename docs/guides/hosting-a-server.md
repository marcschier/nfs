# Hosting a server

A server is an `RpcServer` bound to a TCP endpoint and given one or more `IRpcProgram`s to dispatch to. To serve NFS, host `NfsProgram` — it answers NFS versions 2, 3, and 4 on a single port over one `INfsFileSystem`. To let external clients obtain a root handle, also host the MOUNT program.

## Minimal server

```csharp
using System.Net;
using Nfs.Rpc;
using Nfs.Server;

INfsFileSystem fileSystem = new LocalDiskFileSystem(@"C:\export\data");

var nfs = new RpcServer(new IPEndPoint(IPAddress.Any, 2049), new NfsProgram(fileSystem));
nfs.Start();

Console.WriteLine($"NFS (v2/v3/v4) listening on {nfs.LocalEndPoint}.");
Console.ReadLine();

await nfs.DisposeAsync();
```

`RpcServer.Start()` begins accepting connections on a background loop; `DisposeAsync` stops the listener and drains in-flight connections. Binding to port `0` lets the OS choose a free port, which `LocalEndPoint` then reports — handy for tests.

## Adding MOUNT (for v2/v3 clients)

NFS v2 and v3 clients first call the MOUNT service (program 100005) to turn an export path into a root handle. Host `Nfs3MountProgram` alongside the NFS program, registering one or more export paths:

```csharp
using Nfs.Server;

var mountProgram = new Nfs3MountProgram("/export/data", fileSystem);
mountProgram.AddExport("/export/backup", backupFileSystem); // register additional exports

var mount = new RpcServer(new IPEndPoint(IPAddress.Any, 635), mountProgram);
mount.Start();
```

A single `RpcServer` can dispatch several programs at once if you pass an `IEnumerable<IRpcProgram>`; that is the usual way to co-locate NFS and MOUNT on one process. NFS v4 has no separate MOUNT step — its clients use `PUTROOTFH` to reach the export root — so a v4-only deployment can skip the MOUNT program entirely.

## Hosting a single version

`NfsProgram` is a convenience that bundles all supported versions. If you want to expose only one, host the per-version program directly:

```csharp
var v3Only = new RpcServer(endpoint, new Nfs3Program(fileSystem));
```

`Nfs2Program`, `Nfs3Program`, and `Nfs4Program` each report RPC `PROG_MISMATCH` for versions they do not implement, so a client negotiating versions will fall back correctly.

## Ports and privileges

The standard NFS port is 2049 and the standard MOUNT port is 635, but the library binds wherever you tell it. On Unix, binding ports below 1024 requires privilege; many clients also insist that the server reply from a privileged port unless mounted with an `insecure`/`nolock` option. For local development and tests, bind high ports and point clients at them explicitly. Production deployments that must interoperate with stock OS clients will generally register with `rpcbind` and listen on the standard ports.

## Registering with rpcbind

Clients that do not know the port ask `rpcbind` (portmap, program 100000) for it. The library ships a `PortmapClient` for **querying** a port, a `PortmapServer` so a process can advertise its own programs, and `PortmapRegistration` for best-effort SET/UNSET against a system `rpcbind` (the local-disk server sample exposes this via `--register`). You can also run the server on the well-known ports (2049 for NFS, 635 for MOUNT) so clients find it without a portmap lookup, or have clients connect to a known port directly.

## Threading and lifetime

`RpcServer` handles each connection on its own asynchronous task; your `INfsFileSystem` must therefore be safe for concurrent calls. The in-box `InMemoryFileSystem` and `LocalDiskFileSystem` guard their mutable state. Keep one `RpcServer` per listening endpoint for the lifetime of the process and dispose it on shutdown.

## A runnable example

`samples/Nfs.LocalDiskServer` is a complete, NativeAOT-publishable program that exports a directory over NFS v2/v3/v4 plus MOUNT, runs a quick self-check against itself, and prints the exact `mount` command for an external client:

```sh
# Export a freshly created temp directory, self-check, and exit:
dotnet run --project samples/Nfs.LocalDiskServer

# Export a real directory and keep serving (Ctrl+C to stop):
dotnet run --project samples/Nfs.LocalDiskServer -- /srv/share --serve --nfs-port 2049 --mount-port 635
```
