# Feature and support matrix

This matrix records exactly what is implemented and how it has been verified. It is deliberately conservative: a capability is only listed as supported if there is code **and** a passing test for it. "Tested" here means automated loopback tests (our client against our server over real TCP) plus wire round-trip and golden-byte tests. Cross-implementation interop is also exercised: an independent Python NFSv3/MOUNT client drives the server, and a CI job mounts the server with the real Linux kernel NFS client (`mount -t nfs`) over `rpcbind` and runs a read/write/rename/delete cycle.

Legend: ✅ implemented and tested · 🟡 partial · — not applicable.

## Protocol versions

| Version | Spec | Client | Server | Notes |
|---|---|---|---|---|
| NFS v2 | RFC 1094 | ✅ | ✅ | All procedures. Fixed 32-byte handles, 32-bit sizes, 8 KiB max transfer. |
| NFS v3 | RFC 1813 | ✅ | ✅ | All procedures; `MKNOD` creates FIFO/socket nodes and returns `NFS3ERR_NOTSUPP` for device files. |
| NFS v4.0 | RFC 7530 | ✅ | ✅ | Full COMPOUND op set: filehandle ops, namespace (LOOKUP/LOOKUPP/CREATE/REMOVE/RENAME/LINK/OPENATTR), data (READ/WRITE/COMMIT), OPEN family (UNCHECKED/GUARDED/EXCLUSIVE4, OPEN_CONFIRM/OPEN_DOWNGRADE/CLOSE), byte-range LOCK/LOCKT/LOCKU, VERIFY/NVERIFY, SECINFO, SETCLIENTID(+CONFIRM)/RENEW, lease expiry, reboot grace + stable-storage recovery, READ/WRITE delegations + DELEGRETURN + live CB_RECALL, and ACLs. (RELEASE_LOCKOWNER and DELEGPURGE are not implemented.) |
| NFS v4.1 | RFC 8881 | ✅ | ✅ | EXCHANGE_ID, CREATE_SESSION, SEQUENCE (slot reply cache), BIND_CONN_TO_SESSION, BACKCHANNEL_CTL, FREE_STATEID, TEST_STATEID, DESTROY_SESSION/CLIENTID, RECLAIM_COMPLETE, pNFS files layout with **multi-DS striping** (client fan-out), and a session back channel (CB_SEQUENCE/CB_COMPOUND) delivering CB_RECALL/CB_OFFLOAD/CB_NOTIFY_LOCK. (GET_DIR_DELEGATION / WANT_DELEGATION and trunking are not modeled.) |
| NFS v4.2 | RFC 7862 | 🟡 | 🟡 | minorversion 2 + ALLOCATE, DEALLOCATE, SEEK, COPY (synchronous + **asynchronous offload**: COPY_NOTIFY / OFFLOAD_STATUS / OFFLOAD_CANCEL with CB_OFFLOAD completion), READ_PLUS, CLONE, and RFC 8276 extended attributes (GET/SET/LIST/REMOVEXATTR). Inter-server copy performs a real destination-pull over RPC (the destination opens an RPC connection to the source server and READs the range), exercised between two loopback server instances. |

## Transports and RPC

| Capability | Status | Notes |
|---|---|---|
| TCP (record marking) | ✅ | RFC 5531 framing over `System.IO.Pipelines`. |
| UDP | ✅ | Datagram client/server with retransmission and a duplicate-request cache. |
| `AUTH_NONE` | ✅ | Default. |
| `AUTH_SYS` | ✅ | Client credential builder (`AuthSys.Create`). |
| `RPCSEC_GSS` / Kerberos | 🟡 | RFC 2203 framing + handshake + pluggable GSS provider; a loopback fake mechanism exercises none/integrity/privacy in the offline suite. The **real Kerberos mechanism is implemented on both platforms** — Linux via `libgssapi_krb5` and Windows via `secur32.dll` SSPI (both `[LibraryImport]`, AOT-safe). Linux is verified by a CI job that stands up an MIT KDC + `nfs/...` keytab (handshake + MIC + wrap/unwrap); Windows is verified by a loopback SSPI (Negotiate) test. Domain-joined Windows Kerberos (AD SPN) is not exercised in CI. |
| rpcbind / portmap query | ✅ | `PortmapClient.GetPortAsync` (RFC 1833). |
| rpcbind / portmap server | ✅ | `PortmapServer` (NULL/SET/UNSET/GETPORT/DUMP) lets a process advertise its programs; `rpcinfo -p`-compatible. |
| rpcbind registration with system portmap | ✅ | `PortmapRegistration` SET/UNSET against a system rpcbind (sample `--register`), exercised in CI (`kernel-interop.yml` verifies the NFS/MOUNT registrations with `rpcinfo -p` and `showmount -e`). |
| MOUNT v3 (program 100005) | ✅ | Client + server, including EXPORT and DUMP (so `showmount -e`/`-a` work). |
| NLM v4 (program 100021) | ✅ | Client + server: TEST/LOCK/UNLOCK/CANCEL, **blocking** locks with NLM_GRANTED callbacks, and SM_NOTIFY-driven recovery. |
| NSM (program 100024) | ✅ | `Nsm1Program`/`Nsm1Client`: SM_STAT/MON/UNMON/UNMON_ALL/NOTIFY status monitor. |

## NFS v3 procedures

| Procedure | Client | Server | Procedure | Client | Server |
|---|---|---|---|---|---|
| NULL | ✅ | ✅ | READLINK | ✅ | ✅ |
| GETATTR | ✅ | ✅ | SYMLINK | ✅ | ✅ |
| SETATTR | ✅ | ✅ | LINK | ✅ | ✅ |
| LOOKUP | ✅ | ✅ | READDIR | ✅ | ✅ |
| ACCESS | ✅ | ✅ | READDIRPLUS | ✅ | ✅ |
| READ | ✅ | ✅ | FSSTAT | ✅ | ✅ |
| WRITE | ✅ | ✅ | FSINFO | ✅ | ✅ |
| CREATE | ✅ | ✅ | PATHCONF | ✅ | ✅ |
| MKDIR | ✅ | ✅ | COMMIT | ✅ | ✅ |
| REMOVE | ✅ | ✅ | RENAME | ✅ | ✅ |
| RMDIR | ✅ | ✅ | MKNOD | ✅ | ✅ |

## NFS v2 procedures

| Procedure | Client | Server | Procedure | Client | Server |
|---|---|---|---|---|---|
| NULL | ✅ | ✅ | CREATE | ✅ | ✅ |
| GETATTR | ✅ | ✅ | REMOVE | ✅ | ✅ |
| SETATTR | ✅ | ✅ | RENAME | ✅ | ✅ |
| LOOKUP | ✅ | ✅ | LINK | ✅ | ✅ |
| READLINK | ✅ | ✅ | SYMLINK | ✅ | ✅ |
| READ | ✅ | ✅ | MKDIR | ✅ | ✅ |
| WRITE | ✅ | ✅ | RMDIR | ✅ | ✅ |
| READDIR | ✅ | ✅ | STATFS | ✅ | ✅ |

## NFS v4.0 operations

| Operation | Client | Server | Operation | Client | Server |
|---|---|---|---|---|---|
| COMPOUND | ✅ | ✅ | READDIR | ✅ | ✅ |
| PUTROOTFH | ✅ | ✅ | READLINK | ✅ | ✅ |
| PUTFH | ✅ | ✅ | REMOVE | ✅ | ✅ |
| GETFH | ✅ | ✅ | RENAME | ✅ | ✅ |
| SAVEFH | ✅ | ✅ | CREATE (dir/symlink) | ✅ | ✅ |
| RESTOREFH | ✅ | ✅ | SETATTR | ✅ | ✅ |
| LOOKUP | ✅ | ✅ | OPEN / CLOSE | ✅ | ✅ |
| GETATTR | ✅ | ✅ | OPEN_CONFIRM / OPEN_DOWNGRADE | ✅ | ✅ |
| ACCESS | ✅ | ✅ | SETCLIENTID (+CONFIRM) | ✅ | ✅ |
| READ | ✅ | ✅ | RENEW | ✅ | ✅ |
| WRITE | ✅ | ✅ | LOCK / LOCKT / LOCKU | ✅ | ✅ |
| COMMIT | ✅ | ✅ | LOOKUPP / SECINFO | ✅ | ✅ |
| LINK | ✅ | ✅ | VERIFY / NVERIFY | ✅ | ✅ |
| OPENATTR | ✅ | ✅ | delegations / blocking locks | ✅ | ✅ |

OPEN supports CLAIM_NULL with UNCHECKED/GUARDED **and EXCLUSIVE4** create modes (the create verifier makes retried exclusive creates idempotent and rejects a conflicting verifier with `NFS4ERR_EXIST`) and grants **READ and WRITE** delegations (returned via DELEGRETURN). `OPEN_DOWNGRADE` narrows an open's share access/deny; `VERIFY`/`NVERIFY` do atomic attribute comparison (`NFS4ERR_NOT_SAME`/`NFS4ERR_SAME`). Byte-range locks (LOCK/LOCKT/LOCKU) do conflict detection across lock-owners; a denied blocking lock registers a waiter that is notified via `CB_NOTIFY_LOCK` (v4.1) when the conflicting lock is released, so the client retries promptly. A `TimeProvider`-driven lease manager expires idle client state, and the server starts in a reboot-grace period that rejects non-reclaim OPEN/LOCK with `NFS4ERR_GRACE` until `RECLAIM_COMPLETE` or grace expiry. A conflicting OPEN against an outstanding delegation drives a live `CB_RECALL` (over the v4.0 CB port, or the real v4.1 single-connection back channel) — returning `NFS4ERR_DELAY` while the recall is in flight and revoking the delegation after a `TimeProvider` timeout. `LOOKUPP` and `SECINFO`/`SECINFO_NO_NAME` are supported (the latter advertises AUTH_NONE/AUTH_SYS plus RPCSEC_GSS triples when GSS is enabled). Client recovery records are persisted to pluggable stable storage so the grace period and reclaims survive a server restart.

NFS v4 `fattr4` attributes currently encoded/decoded: `supported_attrs`, `type`, `fh_expire_type`, `change`, `size`, `link_support`, `symlink_support`, `named_attr`, `fsid`, `unique_handles`, `lease_time`, `rdattr_error`, `acl`, `aclsupport`, `filehandle`, `fileid`, `maxfilesize`, `maxlink`, `maxname`, `maxread`, `maxwrite`, `mode`, `numlinks`, `owner`, `owner_group`, `rawdev`, `space_used`, `time_access`, `time_metadata`, `time_modify`.

## Access control and extended attributes

| Capability | Status | Notes |
|---|---|---|
| NFSv4 ACLs (`acl` / `aclsupport`) | ✅ | `nfsace4` ALLOW/DENY entries encoded/decoded via GETATTR/SETATTR; `aclsupport` advertises ALLOW+DENY. `NfsAccessControlEvaluator` computes the effective allow/deny mask for a principal. Backends store ACLs (InMemory exact; LocalDisk synthesizes from mode bits and retains set ACLs in memory — host-OS enforcement is not claimed). |
| Extended attributes (RFC 8276) | ✅ | `GETXATTR` / `SETXATTR` / `LISTXATTRS` / `REMOVEXATTR` with per-object name→value storage and size limits. |

## Client surface

| Capability | Status | Notes |
|---|---|---|
| Low-level per-version clients | ✅ | `Nfs2Client`, `Nfs3Client`, `Nfs4Client`. |
| High-level path-based client | ✅ | `NfsClient` (read/write/list/stat/mkdir/delete) over NFSv3 or NFSv4, selected by negotiation. |
| Capability discovery / version negotiation | ✅ | `NfsClient.ProbeVersionsAsync` reports supported versions; `NfsClient.ConnectNegotiatedAsync` auto-selects the highest mutually supported version (NFSv4 via `PUTROOTFH`/`LOOKUP`, v3/v2 via MOUNT+portmap) with an optional explicit override. |

## NFS v4.1 sessions

| Operation | Client | Server | Notes |
|---|---|---|---|
| EXCHANGE_ID | ✅ | ✅ | SP4_NONE only. |
| CREATE_SESSION | ✅ | ✅ | Fore/back channel attributes echoed. |
| SEQUENCE | ✅ | ✅ | Per-slot exactly-once reply cache. |
| DESTROY_SESSION / DESTROY_CLIENTID | ✅ | ✅ | |
| BIND_CONN_TO_SESSION / BACKCHANNEL_CTL | ✅ | ✅ | Bind a connection to a session's fore/back channel; update the back-channel callback program. |
| FREE_STATEID / TEST_STATEID | ✅ | ✅ | Release a lock stateid (`NFS4ERR_LOCKS_HELD` if held); report per-stateid validity. |
| RECLAIM_COMPLETE | ✅ | ✅ | Accepted as a no-op. |
| pNFS (LAYOUT*, GETDEVICE*) | ✅ | ✅ | Files layout with **multi-DS striping**: GETDEVICEINFO returns multiple data-server addresses + a round-robin stripe-index map, LAYOUTGET returns a dense files layout with a configurable stripe unit, LAYOUTCOMMIT/RETURN. The `Nfs4Client` pNFS API (`GetDeviceInfoAsync`/`LayoutGetAsync`/`ReadStripedAsync`/`WriteStripedAsync`) follows the layout, fans I/O out across the data servers per the stripe map, and falls back to MDS I/O when no layout is available. Exercised across ≥2 in-process DS endpoints over loopback. |
| Back channel / delegations | ✅ | ✅ | v4.0 CB_RECALL over the client's CB port; v4.1 delivers CB_RECALL / CB_OFFLOAD / CB_NOTIFY_LOCK over the **real single fore-channel connection** — `RpcDuplexConnection` demultiplexes inbound callback CALLs from fore-channel replies by xid. READ **and WRITE** delegations are granted and recalled. |

A version 4.1 COMPOUND that begins with SEQUENCE is sequenced through the session reply cache (retransmissions replay the cached reply); session-establishing COMPOUNDs (EXCHANGE_ID, CREATE_SESSION) run without SEQUENCE. Lease expiry and trunking are not modeled.

## NFS v4.2 operations

| Operation | Client | Server | Notes |
|---|---|---|---|
| ALLOCATE | ✅ | ✅ | Extends the file (zero-fill); no sparse metadata. |
| DEALLOCATE | ✅ | ✅ | Emulated by zeroing the range. |
| SEEK | ✅ | ✅ | File treated as wholly data with an implicit hole at EOF. |
| COPY (intra-server) | ✅ | ✅ | Server-side read+write copy; `write_response4`. |
| READ_PLUS | ✅ | ✅ | Returns a single DATA segment (no sparse metadata). |
| CLONE | ✅ | ✅ | Emulated as a copy on non-CoW backends. |

Version 4.2 operations run inside a minorversion-2 SEQUENCE-led COMPOUND, reusing the 4.1 session machinery.

## Storage backends

| Backend | Status | Notes |
|---|---|---|
| `InMemoryFileSystem` | ✅ | Full reference backend incl. hard links and symlinks. |
| `LocalDiskFileSystem` | ✅ | Real directory export; path-traversal guarded; handles not stable across process restarts. |

## Locking and recovery

| Capability | Status | Notes |
|---|---|---|
| NLM / NSM (v2/v3 advisory locks) | ✅ | NLM v4 TEST/LOCK/UNLOCK/CANCEL with conflict detection, **blocking** locks granted via NLM_GRANTED callbacks, and NSM (100024) SM_NOTIFY-driven lock recovery. |
| NFS v4 byte-range locks | ✅ | LOCK/LOCKT/LOCKU with cross-owner conflict detection; denied blocking waiters are notified via `CB_NOTIFY_LOCK` (v4.1) on release so they retry promptly. |
| Server reboot grace / recovery | ✅ | Reboot grace (`NFS4ERR_GRACE` / `NFS4ERR_NO_GRACE`) + `RECLAIM_COMPLETE` + `TimeProvider` lease expiry, with client recovery records persisted to pluggable **stable storage** (`IStableStorage`, file-backed default) so only previously-known clients may reclaim after a restart. |

## Performance

| Capability | Status | Notes |
|---|---|---|
| Benchmarks | ✅ | `benchmarks/Nfs.Benchmarks` (BenchmarkDotNet): GETATTR/LOOKUP latency, READ/WRITE throughput over loopback, and XDR codec micro-benchmarks. See `docs/performance.md`. |
| Hot-path allocation reduction | ✅ | NFSv2/v3 WRITE ingest slices the RPC argument buffer (no `ToArray`); READ and COPY use pooled buffers (`PooledBufferWriter` over `ArrayPool<byte>`, returned once awaited writes complete); v4 keeps a safe copy only where COMPOUND buffer lifetimes require it. `benchmarks/Nfs.Benchmarks` adds `[MemoryDiagnoser]` allocation benchmarks for READ/WRITE/COPY. |

## Verification tiers in CI

1. **Unit / protocol / golden-vector** — XDR round-trips, RPC framing, generator snapshots, known-good wire bytes. Runs everywhere.
2. **Loopback integration** — our client against our server, per version, over real TCP and UDP.
3. **NativeAOT smoke** — publish and run the loopback sample as a native binary.
4. **Cross-implementation interop** — an independent, from-scratch Python NFSv3/MOUNT client (`eng/interop/`) drives the server (run natively on Linux) and round-trips MOUNT + GETATTR/LOOKUP/READ. This validates the wire protocol against a separate implementation; run it with `eng/interop/run-interop.sh`.
5. **Kernel mount** (Linux `mount -t nfs`) — the `.github/workflows/kernel-interop.yml` workflow runs on GitHub-hosted `ubuntu-latest`: it installs `nfs-common`/`rpcbind`, publishes and starts the local-disk server (registering with the system `rpcbind`), verifies the registrations with `rpcinfo -p`/`showmount -e`, then performs a real `sudo mount -t nfs` and asserts a full read/write/rename/delete/unmount cycle (`eng/interop/kernel-mount.sh`). This exercises our server against the actual Linux kernel NFS client. A Windows NFS-client lane and privileged self-hosted variants remain optional.
6. **Kerberos / RPCSEC_GSS** — the `.github/workflows/gss-kerberos.yml` workflow stands up an MIT KDC on `ubuntu-latest` (`eng/interop/krb5-setup.sh`: realm, client principal, `nfs/...` service keytab) and runs a gated integration test that performs a real `libgssapi_krb5` RPCSEC_GSS handshake plus MIC (integrity) and wrap/unwrap (privacy) round-trips. On Windows, a loopback SSPI (Negotiate) test exercises the `secur32.dll` mechanism's handshake + MIC + wrap/unwrap. The offline suite uses a loopback mechanism and stays green without a KDC.

This document is the single source of truth for support claims. Do not assert compliance beyond what is marked ✅ here.
