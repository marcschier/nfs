# NativeAOT

The **net8.0, net9.0, and net10.0** builds of the whole stack are NativeAOT-compatible. There is no runtime-reflection-based serialization, no `MakeGenericMethod`, no `Activator.CreateInstance` on protocol types, and no dynamic code generation. Every XDR codec is either hand-written or emitted at compile time by the source generator as ordinary static code, so the trimmer and the AOT compiler can see and keep exactly what is used.

> **netstandard2.0/2.1 are not AOT targets.** .NET Standard lacks static-virtual interface members, so on those targets the generic codec factory falls back to a cached reflection→delegate binding (`XdrDecoder`), and the source-generated P/Invoke (`LibraryImport`) is replaced with classic `DllImport`. These builds exist for compatibility (.NET Framework, Mono, Unity) and rely on source-only polyfills; they are not reflection-free. The modern (net8.0+) builds are unaffected and remain the AOT path.

## Publishing

```sh
dotnet publish samples/Nfs.Nfsv3Loopback/Nfs.Nfsv3Loopback.csproj -c Release -r win-x64
# or linux-x64, osx-arm64, etc.
```

The loopback sample publishes to a self-contained native binary of roughly 2 MB that starts an in-memory NFS v3 server, drives it with the client, and exits. The continuous-integration pipeline publishes and runs it as an AOT smoke test on every change, which keeps the reflection-free guarantee honest.

## Writing AOT-safe code against the library

If you build your own client, server, or `INfsFileSystem`, you stay AOT-safe by following the same conventions the library uses:

- Keep wire contracts as `[XdrType]` partial types or hand-written `IXdrSerializable<TSelf>` implementations. Do not serialize protocol objects with reflection-based serializers.
- Treat `XdrReader` and `XdrWriter` as `ref struct`s: decode and encode in synchronous helpers, never across an `await`, and never capture them in a closure.
- Avoid `MakeGenericMethod`, `Activator.CreateInstance`, and reflection over your handlers. Dispatch with explicit `switch` statements, as the per-version programs do.

## Analyzer enforcement

The projects build with `IsAotCompatible` set and treat warnings as errors, so AOT-incompatibility warnings (the `IL` series) fail the build rather than slipping through. If you add a dependency or a pattern that the AOT analyzers flag, the compile breaks immediately.
