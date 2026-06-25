# Performance benchmarks

The repository includes a BenchmarkDotNet console app for repeatable local performance checks of the NFS hot paths.

Run the benchmarks from the repository root:

```powershell
dotnet run -c Release --project benchmarks/Nfs.Benchmarks
```

The suite hosts `Nfs3Program` over loopback TCP with an `InMemoryFileSystem` behind `RpcServer`, then drives it through the public `Nfs3Client`. It measures GETATTR latency, LOOKUP latency, READ throughput at 64 KiB and 1 MiB, WRITE throughput at 1 MiB, a buffered 1 MiB COPY-style loop, and an XDR reader/writer round-trip for `Nfs3FileAttributes`.

BenchmarkDotNet reports timing and allocation columns. Run on an otherwise idle machine, use Release configuration, and compare results from the same hardware/OS/runtime when evaluating changes.

The data path threads caller-provided buffers through backend reads and decodes NFSv2/v3 WRITE payloads as slices of the RPC argument memory, with READ and COPY using pooled buffers (`ArrayPool<byte>` via `PooledBufferWriter`). A smoke run on .NET 10 (`--filter *Read64KibThroughputAsync* --job short --warmupCount 1 --iterationCount 1`) completed successfully and reported `Read64KibThroughputAsync` at 840.2 us with 644.72 KiB allocated per operation; use a full before/after run on the same machine for stable deltas.
