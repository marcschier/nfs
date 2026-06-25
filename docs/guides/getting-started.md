# Getting started

> This guide covers building the repository and the conventions every contributor should know.

## Prerequisites

- The **.NET 10 SDK** (the exact version is pinned in `global.json`).
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
