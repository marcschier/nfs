# Nfs

[![CI](https://github.com/marcschier/nfs/actions/workflows/ci.yml/badge.svg)](https://github.com/marcschier/nfs/actions/workflows/ci.yml)
[![NuGet Nfs.Client](https://img.shields.io/nuget/v/Nfs.Client?label=NuGet%20Nfs.Client)](https://www.nuget.org/packages/Nfs.Client)
[![NuGet Nfs.Server](https://img.shields.io/nuget/v/Nfs.Server?label=NuGet%20Nfs.Server)](https://www.nuget.org/packages/Nfs.Server)
[![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-Nfs.*-2188ff?logo=github)](https://github.com/marcschier/nfs/packages)

A modern, idiomatic .NET 10 **NFS client and server** library. It implements the ONC/RPC + XDR stack and the NFS protocols (v2, v3, and v4.0/4.1/4.2) from the ground up, with an emphasis on **protocol compliance and interoperability** with existing real-world implementations (the Linux kernel client and server, NFS-Ganesha, and the Windows NFS client). Performance is a close second, built on `Span<T>`, `BinaryPrimitives`, ref structs, and `System.IO.Pipelines`.

The whole stack is **NativeAOT-compatible** — there is no runtime-reflection-based serialization. XDR codecs are produced by a Roslyn source generator.

> The library is implemented from scratch. The previous (legacy) NekoDrive code has been removed; its protocol `.x` definitions are preserved under [`docs/reference/xdr`](docs/reference/xdr) as compliance references.

## 📚 Documentation

Developer documentation lives in [`docs/`](docs/README.md):

- 🏗️ **[Architecture overview](docs/architecture/overview.md)** — how the RPC, XDR, and NFS layers fit together.
- 📖 **Guides** — [Getting started](docs/guides/getting-started.md) · [Using the client](docs/guides/using-the-client.md) · [Hosting a server](docs/guides/hosting-a-server.md) · [Implementing a filesystem](docs/guides/implementing-a-filesystem.md) · [NativeAOT](docs/guides/native-aot.md)
- ✅ **[Feature matrix](docs/feature-matrix.md)** — exactly which protocols, operations, and capabilities are supported (the single source of truth for support claims).
- ⚡ **[Performance](docs/performance.md)** — benchmarks and the pooled, zero-copy data path.
- 🔐 **[RPCSEC_GSS / Kerberos](docs/rpcsec-gss-kerberos.md)** — the security layer.

## 🧱 Repository layout

| Path | Contents |
| --- | --- |
| `src/` | Shipping libraries (`Nfs.*`). |
| `tests/` | Unit, golden-vector, loopback, and interop tests. |
| `samples/` | Runnable client and server samples. |
| `docs/` | Architecture notes, developer guides, and protocol references. |

## 🛠️ Building

Requires the **.NET 10 SDK** (pinned in `global.json`).

```sh
dotnet build Nfs.slnx -c Release
dotnet test  Nfs.slnx -c Release
dotnet format Nfs.slnx --verify-no-changes --severity warn
```

To run a single test project or a single test:

```sh
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj --filter "FullyQualifiedName~XdrConstantsTests"
```

To validate NativeAOT publishing:

```sh
dotnet publish samples/Nfs.AotSmoke/Nfs.AotSmoke.csproj -c Release -r linux-x64
```
