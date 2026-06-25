# Interop verification

This directory holds a **cross-implementation interop check** for the NFS server: a from-scratch
Python NFSv3 + MOUNT client (`nfs3_interop_client.py`) that shares no code with the C# library. It
re-implements ONC/RPC record marking, the RPC call/reply headers (RFC 5531), and the XDR shapes it
needs (RFC 1813), then mounts an export and performs `GETATTR`, `LOOKUP`, and `READ` — asserting the
results. Because the client is an independent implementation, a passing run is real evidence that the
server is correct on the wire, not merely self-consistent with our own client.

## Running it

On any Linux host with the .NET 10 runtime and `python3` (including WSL):

```sh
./eng/interop/run-interop.sh
```

The script starts `samples/Nfs.LocalDiskServer` (which exports a temporary directory containing
`readme.txt` over NFS v2/v3/v4 plus MOUNT), then runs the Python client against it on loopback and
stops the server. A passing run prints:

```
MOUNT /export -> root handle (8 bytes)
GETATTR root -> type=2 (2=directory) mode=0o755
LOOKUP readme.txt -> handle (8 bytes)
READ readme.txt -> 38 bytes, eof=True: b'Hello from the Nfs local-disk server!\n'
INTEROP OK: independent Python client round-tripped MOUNT + NFSv3 GETATTR/LOOKUP/READ
```

## Kernel mount and rpcbind verification

This check validates the server against an **independent userspace implementation** of the protocol.
A full **Linux kernel mount** (`mount -t nfs`) additionally exercises the in-kernel NFS client.
GitHub-hosted `ubuntu-latest` runners provide passwordless `sudo`, so `.github/workflows/kernel-interop.yml`
installs `nfs-common` and `rpcbind`, starts `samples/Nfs.LocalDiskServer` on loopback, verifies the
rpcbind registrations with `rpcinfo -p 127.0.0.1` and `showmount -e 127.0.0.1`, mounts the export with
explicit `port=` and `mountport=`, performs read/write/rename/delete I/O, and unmounts in cleanup.

On a Linux host with NFS client support and `sudo`, the same reusable script can be run directly:

```sh
sudo apt-get install -y nfs-common rpcbind
NFS_PORT=20490 MOUNT_PORT=20491 NFS_VERSION=3 REQUIRE_RPCBIND=1 ./eng/interop/kernel-mount.sh
```

Set `REQUIRE_RPCBIND=0` to keep rpcbind/showmount checks best-effort, or `NFS_VERSION=4` for an
explicit NFSv4 kernel-mount smoke test when the host kernel and server pseudo-filesystem behavior
support it.
