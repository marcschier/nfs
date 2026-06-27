# Nfs

[![CI](https://github.com/marcschier/nfs/actions/workflows/ci.yml/badge.svg)](https://github.com/marcschier/nfs/actions/workflows/ci.yml)
[![NuGet Nfs](https://img.shields.io/nuget/v/Nfs?label=NuGet%20Nfs)](https://www.nuget.org/packages/Nfs)
[![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-Nfs.*-2188ff?logo=github)](https://github.com/marcschier/nfs/packages)

A modern, idiomatic **.NET NFS client and server** library, multi-targeting **net8.0, net9.0, and net10.0** plus **netstandard2.0 and netstandard2.1** (for .NET Framework, Mono, and Unity consumers). It implements the ONC/RPC + XDR stack and the NFS protocols (v2, v3, and v4.0/4.1/4.2) from the ground up, with an emphasis on **protocol compliance and interoperability** with existing real-world implementations (the Linux kernel client and server, NFS-Ganesha, and the Windows NFS client). Performance is a close second, built on `Span<T>`, `BinaryPrimitives`, ref structs, and `System.IO.Pipelines`.

The whole stack is **NativeAOT-compatible** — there is no runtime-reflection-based serialization. XDR codecs are produced by a Roslyn source generator.

## 📦 Installation

The whole stack ships as a single package on nuget.org:

```sh
dotnet add package Nfs
```

`Nfs` bundles every component assembly (`Nfs.Client`, `Nfs.Server`, `Nfs.Rpc`, `Nfs.Xdr`, the protocol types, MOUNT, NLM/NSM) and has **no external NuGet dependencies** — NativeAOT trimming drops whatever you don't use. The individual `Nfs.*` packages are also published to this repository's [GitHub Packages](https://github.com/marcschier/nfs/packages) feed for fine-grained or internal consumption.

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

Building requires the **.NET 10 SDK** (pinned in `global.json`); it produces all target frameworks. The shipping libraries multi-target **net8.0, net9.0, net10.0, netstandard2.0, and netstandard2.1** — running the net8.0/net9.0 test executables additionally needs those runtimes installed. The net8.0/net9.0/net10.0 builds are reflection-free and NativeAOT-safe; the netstandard builds use source-only polyfills (the [`Polyfill`](https://www.nuget.org/packages/Polyfill) package plus BCL backports such as `System.Memory`) and a small reflection-based codec fallback, and are intended for compatibility (.NET Framework, Mono, Unity), not AOT. See [native-aot](docs/guides/native-aot.md).

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
