# Getting started

> This guide covers building the repository and the conventions every contributor should know.

## Prerequisites

- The **.NET 10 SDK** (the exact version is pinned in `global.json`) — it builds every target framework. The libraries multi-target **net8.0, net9.0, net10.0, netstandard2.0, and netstandard2.1**; the **.NET 8 and .NET 9 runtimes** are needed only to *run* the net8.0/net9.0 test executables. (The netstandard builds add source-only polyfills for older consumers; see [native-aot](native-aot.md) for the trade-offs.)
- For NativeAOT publishing: a native toolchain (`clang` + `zlib` on Linux, the MSVC C++ build tools on Windows).

## Everyday commands

```sh
# Restore, build, and test the whole solution.
dotnet build Nfs.slnx -c Release
dotnet test  Nfs.slnx -c Release

# Check formatting/style the same way CI does.
dotnet format Nfs.slnx --verify-no-changes --severity warn

# Run one test project, or one test by name.
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj
dotnet test tests/Nfs.Xdr.Tests/Nfs.Xdr.Tests.csproj --filter "FullyQualifiedName~XdrConstantsTests"
```

## Conventions

- **Allman braces** and file-scoped namespaces; see `.editorconfig`.
- New libraries go under `src/` and inherit `IsAotCompatible`, documentation generation, and packability from `src/Directory.Build.props`. Keep them free of runtime reflection so they stay AOT-safe.
- Test projects go under `tests/` and automatically reference xUnit v3.
- Package versions are added to `Directory.Packages.props` (Central Package Management); do not put versions in individual project files.
- **Strong naming.** Every assembly is full-signed with the committed `nfs.snk` (public key token `a5b998207a9d983e`) via `SignAssembly` in the root `Directory.Build.props`. The key is checked in deliberately — strong naming is an identity, not a secret — so Windows and Linux CI both produce a valid signature with no extra setup. Because a strong-named assembly can only grant `InternalsVisibleTo` to another strong-named one, test projects are signed with the same key, and the friend grant in `src/Nfs.Rpc/Nfs.Rpc.csproj` carries the public key (`Key="$(NfsPublicKey)"`).
