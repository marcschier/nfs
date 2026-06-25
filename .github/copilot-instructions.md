# Nfs — Copilot instructions

`Nfs` is a from-scratch, idiomatic **.NET 10** library implementing an **NFS client and server** (ONC/RPC + XDR, NFS v2/v3/v4.x). Priorities, in order: **protocol compliance / interop** with real implementations (Linux kernel `nfsd`/`mount`, NFS-Ganesha, Windows NFS), then performance (`Span<T>`, `BinaryPrimitives`, ref structs, `System.IO.Pipelines`), then ergonomics. The entire stack must stay **NativeAOT-compatible** — no runtime-reflection serialization.

> The previous legacy WinForms/Dokan NFS client ("NekoDrive") has been removed. Do not resurrect its patterns (Java-style `org.acplt.oncrpc`, `jrpcgen`, etc.). Its protocol `.x` definitions are preserved under `docs/reference/xdr/` as source-generator inputs and compliance references.

## Build / test / lint / AOT (all verified working)

Requires the .NET 10 SDK (pinned in `global.json`). Run from the repo root.

```sh
dotnet build  Nfs.slnx -c Release
dotnet test   Nfs.slnx -c Release
dotnet format Nfs.slnx --verify-no-changes --severity warn      # the CI lint gate
```

Single test project / single test:

```sh
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj --filter "FullyQualifiedName~XdrConstantsTests"
```

NativeAOT publish smoke (proves the stack stays AOT-safe; native toolchain required):

```sh
dotnet publish samples/Nfs.AotSmoke/Nfs.AotSmoke.csproj -c Release -r win-x64   # or linux-x64
```

## Architecture (target layering)

Bottom-up; each layer depends only on those above it. See `docs/architecture/overview.md` for detail. Most layers are planned, not yet built — check `src/` for what currently exists.

`Nfs.Xdr` (ref-struct readers/writers; contiguous span fast path + segmented `ReadOnlySequence` path + blob/streaming for bulk) → `Nfs.Xdr.SourceGenerator` (Roslyn incremental generator; **hybrid input**: RFC `.x` IDL for the big v4 surface, annotated C# `[Xdr*]` types for curated models; emits statically-rooted dispatch, no reflection) → `Nfs.Abstractions` (file handles, status/error mapping, attribute + uid/gid mapping, directory cookie/verifier, `INfsFileSystem`) → `Nfs.Rpc` (RFC 5531; AUTH_NONE/AUTH_SYS with a GSS-ready auth abstraction; TCP record marking over Pipelines, UDP, duplicate-request cache, rpcbind) → `Nfs.Mount`/`Nfs.Nlm`/`Nfs.Nsm` → `Nfs.Protocol.V2|V3|V4` (generated wire types only) → `Nfs.V4.State` (stateids, leases, owners, sessions, reply cache — kept out of the wire contracts) → `Nfs.Client` / `Nfs.Server` (+ local-disk and in-memory backends).

**Core execution rule:** buffer a full RPC record asynchronously (Pipelines), decode synchronously with a `ref struct` reader, encode synchronously into a pooled buffer, then send asynchronously. Ref structs never cross an `await` and never escape codec methods. Bulk `READ`/`WRITE` use the segmented/streaming paths — never force a single contiguous array.

## Conventions

- **Style:** Allman braces, file-scoped namespaces, `Nullable` + `ImplicitUsings` enabled, `LangVersion=latest`. Enforced via `.editorconfig` + `dotnet format` (not the build) and Roslynator analyzers. **All source files use LF** — files authored on Windows must be converted (the `.gitattributes` enforces LF; `dotnet format` fails on CRLF via the ENDOFLINE rule).
- **Layout:** libraries in `src/` (inherit `IsAotCompatible`, XML docs, packability from `src/Directory.Build.props`), tests in `tests/` (auto-reference xUnit v3; `OutputType=Exe`; `CA1707` suppressed so `Method_Scenario_Expectation` names are allowed), samples in `samples/`.
- **Packages:** Central Package Management — add every version to `Directory.Packages.props`, never in a `.csproj`. A repo-local `NuGet.config` pins a single feed (nuget.org); keep it so CPM + machine-global feeds don't collide.
- **Warnings are errors** repo-wide (`TreatWarningsAsErrors=true`), so public members in `src/` need XML docs (CS1591) and code must be AOT-clean.
- **Versioning:** Nerdbank.GitVersioning (`version.json`, currently `0.1-alpha`); CI checkouts need `fetch-depth: 0`.
- **CI** (`.github/workflows/ci.yml`): build+test on ubuntu & windows, a `dotnet format` lint gate, and a Linux NativeAOT publish-and-run smoke. Kernel-level NFS interop (privileged ports, `mount`, `nfsd`) is **not reliable on hosted runners** — it belongs on self-hosted/optional tiers.
```
