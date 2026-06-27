# Architecture overview

`Nfs` is built as a set of layered libraries under `src/`. Each layer depends only on the layers above it in this list, which keeps the dependency graph acyclic and lets the client and server share everything below them.

## Layers

1. **`Nfs.Xdr`** — XDR (RFC 4506) encoding primitives. `ref struct` readers/writers over `ReadOnlySpan<byte>` (contiguous fast path) and `ReadOnlySequence<byte>` (segmented path), big-endian via `BinaryPrimitives`, four-byte alignment, and a blob/streaming escape hatch for large opaque payloads. Bounded by a maximum record size.
2. **`Nfs.Xdr.SourceGenerator`** — a Roslyn incremental generator that emits XDR codecs. It accepts two inputs: canonical RFC `.x` IDL files (used for the large NFSv4 type surface) and annotated C# types (`[Xdr*]` attributes) for curated and higher-level models. It emits explicit, statically-rooted dispatch — no runtime reflection — so the output is NativeAOT-safe.
3. **`Nfs.Abstractions`** — cross-cutting protocol contracts shared by client and server: file-handle representation, status/error-mapping, file attributes and uid/gid identity mapping, ACLs and extended attributes, directory cookie/verifier semantics, and the pluggable `INfsFileSystem` storage interface.
4. **`Nfs.Rpc`** — ONC/RPC (RFC 5531): call/reply messages; pluggable authentication (`AUTH_NONE`, `AUTH_SYS`, and `RPCSEC_GSS` (RFC 2203) framing with a pluggable GSS mechanism); a TCP transport with record marking over `System.IO.Pipelines`; a UDP datagram transport with retransmission and a duplicate-request cache; a single-connection duplex transport that carries the NFSv4.1 back channel; an RPC client (XID correlation; `IRpcClient`) and server (program/version/procedure dispatch); and an rpcbind/portmap (RFC 1833) query client, a portmap server, and best-effort registration with a system rpcbind.
5. **`Nfs.Mount`** — the MOUNT protocol (v1/v3): it turns an export path into a root file handle and answers EXPORT/DUMP.
6. **`Nfs.Nlm` / `Nfs.Nsm`** — the Network Lock Manager (NLM v4, program 100021) and Network Status Monitor (NSM/`statd`, program 100024): advisory and blocking byte-range locks with `NLM_GRANTED` callbacks and `SM_NOTIFY`-driven recovery.
7. **`Nfs.Protocol.V2` / `Nfs.Protocol.V3` / `Nfs.Protocol.V4`** — wire types and procedure/operation definitions (generated or hand-written codecs). These are *types only*; they contain no protocol behavior.
8. **`Nfs.Client`** — the client surface: low-level per-version clients (`Nfs2Client`, `Nfs3Client`, `Nfs4Client`) plus a unified `NfsClient` that negotiates the highest mutually supported version at connect time.
9. **`Nfs.Server`** — the server surface: `NfsProgram` (which serves v2, v3, and v4 on one port), file-handle management, attribute/ACL/xattr mapping, the NFSv4 state engine (`Nfs4StateStore`, `Nfs41SessionStore` — client identity, stateids, open/lock owners, leases, grace and recovery, delegations with back-channel recall, v4.1 sessions and the reply cache, and a single-data-server pNFS files layout), and local-disk and in-memory `INfsFileSystem` backends.

## Core execution principle

Full RPC records are buffered **asynchronously** (Pipelines for TCP), then decoded **synchronously** with ref-struct readers; responses are encoded synchronously into pooled buffers and then sent **asynchronously**. Ref structs therefore never cross an `await` boundary and never escape codec methods. Large `READ`/`WRITE` payloads use the segmented/streaming paths to avoid forced contiguity and large copies.

## Multi-version dispatch

NFS versions 2, 3, and 4 all share RPC program number **100003**, distinguished only by the RPC version field. `NfsProgram` is a single `IRpcProgram` for program 100003 that inspects the RPC version and forwards to `Nfs2Program`, `Nfs3Program`, or `Nfs4Program`, so one server answers all three versions on one port over one `INfsFileSystem`. The per-version programs can also be hosted individually.

## Stateless versus stateful

NFS v2 and v3 are stateless on the server and their handlers live entirely in the per-version programs. NFS v4 introduces server state — client identifiers, stateids, opens, locks, leases, delegations, and v4.1 sessions. That concern is deliberately separated: the v4 wire types carry no semantics, the COMPOUND processor keeps only the per-request current/saved file-handle state, and the durable state lives in the `Nfs4StateStore` / `Nfs41SessionStore` types in `Nfs.Server`. `OPEN`/`CLOSE`, byte-range locks (including blocking waiters notified via `CB_NOTIFY_LOCK`), lease and grace/recovery management, READ and WRITE delegations with back-channel recall, and v4.1 sessions are implemented; see the [feature and support matrix](../feature-matrix.md) for the exact operation-by-operation status.

## Build, style, and AOT

- Target frameworks: the shipping libraries multi-target `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`, and `netstandard2.1`. The same source compiles on every target — the `net8.0`/`net9.0`/`net10.0` builds use no per-framework fallbacks, so their output is unchanged and reflection-free/AOT-safe. The netstandard builds (for .NET Framework, Mono, and Unity consumers) fill API gaps with source-only polyfills (the `Polyfill` package + BCL backports such as `System.Memory`, `System.IO.Pipelines`, `Microsoft.Bcl.*`) and a cached reflection→delegate fallback for the codec factory, since .NET Standard lacks static-virtual interface members; they are therefore **not** NativeAOT targets, and a few disk operations (symbolic links) are unsupported on `netstandard2.0`. Samples and the NativeAOT smoke stay `net10.0`. Every modern build sets `IsAotCompatible` (via `src/Directory.Build.props`) and is verified by a CI job that publishes a sample with `PublishAot` and fails on any IL/trim warning.
- Code style is Allman, enforced via `.editorconfig` and `dotnet format`; Roslynator analyzers are enabled repo-wide.
- Versioning is handled by Nerdbank.GitVersioning; package versions are centrally managed.

## See also

- [Using the client](../guides/using-the-client.md)
- [Implementing a file system](../guides/implementing-a-filesystem.md)
- [Hosting a server](../guides/hosting-a-server.md)
- [NativeAOT](../guides/native-aot.md)
- [Feature and support matrix](../feature-matrix.md)
