# Reference XDR / RPCL definitions

These files are the canonical [XDR](https://www.rfc-editor.org/rfc/rfc4506) / RPC-language
protocol definitions for the NFS family. They serve two purposes:

1. **Source-generator input** — the `.x` files (notably the large NFSv4.1 surface) are fed to
   `Nfs.Xdr.SourceGenerator` to emit idiomatic C# types and codecs, minimizing hand-transcription
   error.
2. **Compliance references** — they are checked against the generated types and against
   golden wire-byte fixtures.

| File | Protocol | Source |
| --- | --- | --- |
| `nfsv2.x` | NFS v2 | RFC 1094 |
| `nfsv2-mount.x` | MOUNT v1 (for NFS v2) | RFC 1094 appendix |
| `nfsv3.x` | NFS v3 | RFC 1813 |
| `nfsv3-mount.x` | MOUNT v3 | RFC 1813 appendix |
| `nfsv4.1.x` | NFS v4.1 | RFC 8881 |

`ORIGIN.txt` is the note that shipped with the original (legacy) copy of these definitions.

> NFS v4.0 (RFC 7530) and v4.2 (RFC 7862) definitions will be added here as those versions are
> implemented. NFS v1 has no public specification and is out of scope.
