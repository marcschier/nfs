using System.Buffers;
using System.Net;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V3;
using Nfs.Rpc;
using Nfs.Server;
using Nfs.Xdr;

namespace Nfs.Benchmarks;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher
            .FromTypes([typeof(Nfs3LoopbackBenchmarks), typeof(NfsCopyBenchmarks), typeof(XdrCodecBenchmarks)])
            .Run(args);
}

[MemoryDiagnoser]
public class Nfs3LoopbackBenchmarks : IAsyncDisposable
{
    private RpcClient? _rpc;
    private RpcServer? _server;
    private byte[] _writeData = [];
    private Nfs3Handle _lookupDirectory;
    private Nfs3Handle _read64KibFile;
    private Nfs3Handle _read1MibFile;
    private Nfs3Handle _root;
    private Nfs3Handle _writeFile;

    [GlobalSetup]
    public async Task SetUpAsync()
    {
        var fileSystem = new InMemoryFileSystem();
        _root = ToNfs3Handle(fileSystem.Root);
        _lookupDirectory = ToNfs3Handle(fileSystem.CreateDirectory(fileSystem.Root, "lookup-dir"));

        byte[] read64KibData = CreateData(64 * 1024);
        byte[] read1MibData = CreateData(1024 * 1024);
        _writeData = CreateData(1024 * 1024);

        _read64KibFile = ToNfs3Handle(fileSystem.CreateFile(fileSystem.Root, "read-64kib.bin", read64KibData));
        _read1MibFile = ToNfs3Handle(fileSystem.CreateFile(fileSystem.Root, "read-1mib.bin", read1MibData));
        _writeFile = ToNfs3Handle(fileSystem.CreateFile(fileSystem.Root, "write-1mib.bin", []));
        _ = fileSystem.CreateFile(fileSystem.Root, "lookup-target", [1, 2, 3, 4]);

        _server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs3Program(fileSystem));
        _server.Start();
        _rpc = await RpcClient.ConnectAsync(_server.LocalEndPoint).ConfigureAwait(false);
        Client = new Nfs3Client(_rpc);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_rpc is not null)
        {
            await _rpc.DisposeAsync().ConfigureAwait(false);
            _rpc = null;
        }

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async ValueTask<uint> GetAttrLatencyAsync()
    {
        Nfs3GetAttrResult result = await Client.GetAttributesAsync(_root).ConfigureAwait(false);
        return EnsureSuccess(result).Mode;
    }

    [Benchmark]
    public async ValueTask<int> LookupLatencyAsync()
    {
        Nfs3LookupResult result = await Client.LookupAsync(_root, "lookup-target").ConfigureAwait(false);
        return EnsureSuccess(result).Data.Length;
    }

    [Benchmark]
    public async ValueTask<int> Read64KibThroughputAsync()
    {
        Nfs3ReadResult result = await Client.ReadAsync(_read64KibFile, 0, 64 * 1024).ConfigureAwait(false);
        return EnsureSuccess(result).Length;
    }

    [Benchmark]
    public async ValueTask<int> Read1MibThroughputAsync()
    {
        Nfs3ReadResult result = await Client.ReadAsync(_read1MibFile, 0, Nfs3.MaxReadSize).ConfigureAwait(false);
        return EnsureSuccess(result).Length;
    }

    [Benchmark]
    public async ValueTask<uint> Write1MibThroughputAsync()
    {
        Nfs3WriteResult result = await Client.WriteAsync(_writeFile, 0, _writeData).ConfigureAwait(false);
        return EnsureSuccess(result);
    }

    private Nfs3Client Client { get; set; } = null!;

    private static byte[] CreateData(int length)
    {
        byte[] data = new byte[length];
        new Random(42).NextBytes(data);
        return data;
    }

    private static Nfs3Handle ToNfs3Handle(NfsFileHandle handle) => new() { Data = handle.ToArray() };

    private static Nfs3FileAttributes EnsureSuccess(Nfs3GetAttrResult result) =>
        result.IsSuccess ? result.Attributes : throw new InvalidOperationException($"GETATTR failed with {result.Status}.");

    private static Nfs3Handle EnsureSuccess(Nfs3LookupResult result) =>
        result.IsSuccess ? result.Ok.Handle : throw new InvalidOperationException($"LOOKUP failed with {result.Status}.");

    private static byte[] EnsureSuccess(Nfs3ReadResult result) =>
        result.IsSuccess ? result.Ok.Data : throw new InvalidOperationException($"READ failed with {result.Status}.");

    private static uint EnsureSuccess(Nfs3WriteResult result) =>
        result.IsSuccess ? result.Ok.Count : throw new InvalidOperationException($"WRITE failed with {result.Status}.");
}

[MemoryDiagnoser]
public class NfsCopyBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new(Nfs4MaxIoSize);
    private InMemoryFileSystem _fileSystem = null!;
    private NfsFileHandle _source;
    private NfsFileHandle _destination;

    [GlobalSetup]
    public void SetUp()
    {
        _fileSystem = new InMemoryFileSystem();
        _source = _fileSystem.CreateFile(_fileSystem.Root, "source.bin", CreateData(1024 * 1024));
        _destination = _fileSystem.CreateFile(_fileSystem.Root, "destination.bin", []);
    }

    [Benchmark]
    public async ValueTask<ulong> Copy1MibBufferedAsync()
    {
        ulong copied = 0;
        while (copied < CopySize)
        {
            _buffer.Clear();
            uint chunk = (uint)Math.Min((ulong)Nfs4MaxIoSize, CopySize - copied);
            NfsBufferedReadResult read = await _fileSystem
                .ReadAsync(_source, copied, chunk, _buffer)
                .ConfigureAwait(false);
            if (read.Count == 0)
            {
                break;
            }

            NfsWriteResult write = await _fileSystem
                .WriteAsync(_destination, copied, _buffer.WrittenMemory)
                .ConfigureAwait(false);
            copied += write.Count;
            if (read.EndOfFile || write.Count < read.Count)
            {
                break;
            }
        }

        return copied;
    }

    private const int Nfs4MaxIoSize = 1024 * 1024;
    private const ulong CopySize = 1024 * 1024;

    private static byte[] CreateData(int length)
    {
        byte[] data = new byte[length];
        new Random(42).NextBytes(data);
        return data;
    }
}

[MemoryDiagnoser]
public class XdrCodecBenchmarks
{
    private readonly Nfs3FileAttributes _attributes = new()
    {
        Type = NfsFileType.Regular,
        Mode = 0x1A4,
        LinkCount = 1,
        Uid = 1000,
        Gid = 1000,
        Size = 1024 * 1024,
        Used = 1024 * 1024,
        Rdev = new Nfs3SpecData { Major = 0, Minor = 0 },
        FileSystemId = 1,
        FileId = 42,
        AccessTime = new Nfs3Time { Seconds = 1_700_000_000, Nanoseconds = 123 },
        ModifyTime = new Nfs3Time { Seconds = 1_700_000_001, Nanoseconds = 456 },
        ChangeTime = new Nfs3Time { Seconds = 1_700_000_002, Nanoseconds = 789 },
    };

    [Benchmark]
    public ulong Nfs3FileAttributesRoundTrip()
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        var writer = new XdrWriter(buffer);
        _attributes.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs3FileAttributes decoded = Nfs3FileAttributes.ReadFrom(ref reader);
        return decoded.Size;
    }
}
