# Documentation

- [`architecture/overview.md`](architecture/overview.md) — how the stack is layered, multi-version dispatch, and the stateless/stateful split.
- [`feature-matrix.md`](feature-matrix.md) — exactly what is implemented and tested. The single source of truth for support claims.
- [`performance.md`](performance.md) — the benchmark suite and the pooled, zero-copy data path.
- [`rpcsec-gss-kerberos.md`](rpcsec-gss-kerberos.md) — the RPCSEC_GSS security layer.
- [`guides/`](guides) — developer how-to guides:
  - [Getting started](guides/getting-started.md) — build, test, and contributor conventions.
  - [Using the client](guides/using-the-client.md) — call NFS v2, v3, and v4 servers.
  - [Implementing a file system](guides/implementing-a-filesystem.md) — export your own storage through `INfsFileSystem`.
  - [Hosting a server](guides/hosting-a-server.md) — run an NFS server and the MOUNT service.
  - [NativeAOT](guides/native-aot.md) — publishing native binaries and staying reflection-free.
- [`reference/xdr/`](reference/xdr) — canonical XDR/RPCL protocol definitions used as source-generator inputs and compliance references.
