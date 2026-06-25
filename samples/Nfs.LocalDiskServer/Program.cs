using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Mount;
using Nfs.Protocol.V2;
using Nfs.Protocol.V3;
using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Server;

// Usage:
//   Nfs.LocalDiskServer [exportPath] [--serve] [--register] [--nfs-port N] [--mount-port N]
//
// With no arguments it exports a freshly created temporary directory, runs a quick self-check
// against itself, prints how to mount it from another machine, and exits. Pass --serve to keep the
// server running (Ctrl+C to stop). Pass --register to best-effort register with local rpcbind.

var options = CommandLineOptions.Parse(args);
string exportPath = options.ExportPath ?? CreateSampleExport();
string exportName = "/export";

INfsFileSystem fileSystem = new LocalDiskFileSystem(exportPath);

// Host NFS (v2/v3/v4) and the MOUNT service. Defaults use high ports so no privilege is required.
await using var nfs = new RpcServer(
    new IPEndPoint(IPAddress.Any, options.NfsPort), new NfsProgram(fileSystem));
await using var mount = new RpcServer(
    new IPEndPoint(IPAddress.Any, options.MountPort), new Nfs3MountProgram(exportName, fileSystem));
nfs.Start();
mount.Start();

var registrations = new List<PortmapRegistration>();
try
{
    if (options.Register)
    {
        await RegisterWithRpcbindAsync(registrations, nfs.LocalEndPoint.Port, mount.LocalEndPoint.Port);
    }

    Console.WriteLine($"Exporting '{exportPath}' as '{exportName}'.");
    Console.WriteLine($"NFS   (v2/v3/v4) listening on {nfs.LocalEndPoint}.");
    Console.WriteLine($"MOUNT            listening on {mount.LocalEndPoint}.");
    if (options.Register)
    {
        Console.WriteLine("rpcbind registration requested at 127.0.0.1:111 (best-effort).");
    }

    Console.WriteLine();
    Console.WriteLine("From a Linux client (matching the ports above):");
    Console.WriteLine(
        $"  sudo mount -t nfs -o vers=3,tcp,port={nfs.LocalEndPoint.Port},mountport={mount.LocalEndPoint.Port},nolock " +
        $"<server>:{exportName} /mnt/point");
    Console.WriteLine();

    await SelfCheckAsync(
        new IPEndPoint(IPAddress.Loopback, mount.LocalEndPoint.Port),
        new IPEndPoint(IPAddress.Loopback, nfs.LocalEndPoint.Port),
        exportName);

    if (options.Serve)
    {
        Console.WriteLine("Serving. Press Ctrl+C to stop.");
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.TrySetResult();
        };
        await stop.Task;
    }
}
finally
{
    foreach (PortmapRegistration registration in registrations)
    {
        await registration.DisposeAsync();
    }
}

static string CreateSampleExport()
{
    string path = Path.Combine(Path.GetTempPath(), "nfs-export-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, "readme.txt"), "Hello from the Nfs local-disk server!\n");
    Directory.CreateDirectory(Path.Combine(path, "docs"));
    return path;
}

static async Task SelfCheckAsync(IPEndPoint mountEndPoint, IPEndPoint nfsEndPoint, string exportName)
{
    Console.WriteLine("Self-check:");

    await using var mountRpc = await RpcClient.ConnectAsync(mountEndPoint);
    var mountClient = new Mount3Client(mountRpc);
    Mount3MountResult mounted = await mountClient.MountAsync(exportName);
    if (!mounted.IsSuccess)
    {
        Console.WriteLine($"  MOUNT {exportName} -> {mounted.Status}");
        return;
    }

    var root = new Nfs3Handle { Data = mounted.Ok.Handle };
    Console.WriteLine($"  MOUNT {exportName} -> handle ({root.Data.Length} bytes)");

    await using var nfsRpc = await RpcClient.ConnectAsync(nfsEndPoint);
    var client = new Nfs3Client(nfsRpc);

    Nfs3ReadDirResult listing = await client.ReadDirectoryAsync(root);
    string[] names = listing.Ok.Entries.Select(e => e.Name).ToArray();
    Console.WriteLine($"  READDIR / -> {string.Join(", ", names)}");

    Nfs3LookupResult lookup = await client.LookupAsync(root, "readme.txt");
    if (lookup.IsSuccess)
    {
        Nfs3ReadResult read = await client.ReadAsync(lookup.Ok.Handle, 0, 1024);
        Console.WriteLine($"  READ readme.txt -> \"{Encoding.UTF8.GetString(read.Ok.Data).TrimEnd()}\"");
    }

    Console.WriteLine("  OK");
    Console.WriteLine();
}

static async Task RegisterWithRpcbindAsync(List<PortmapRegistration> registrations, int nfsPort, int mountPort)
{
    var endpoint = new IPEndPoint(IPAddress.Loopback, Portmap.WellKnownPort);
    registrations.Add(await PortmapRegistration.RegisterAsync(
        endpoint, Nfs2.Program, Nfs2.ProtocolVersion, PortmapProtocol.Tcp, nfsPort));
    registrations.Add(await PortmapRegistration.RegisterAsync(
        endpoint, Nfs3.Program, Nfs3.ProtocolVersion, PortmapProtocol.Tcp, nfsPort));
    registrations.Add(await PortmapRegistration.RegisterAsync(
        endpoint, Nfs4.Program, Nfs4.ProtocolVersion, PortmapProtocol.Tcp, nfsPort));
    registrations.Add(await PortmapRegistration.RegisterAsync(
        endpoint, Mount3.Program, Mount3.ProtocolVersion, PortmapProtocol.Tcp, mountPort));
}

internal sealed record CommandLineOptions(string? ExportPath, bool Serve, bool Register, int NfsPort, int MountPort)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? exportPath = null;
        bool serve = false;
        bool register = false;
        int nfsPort = 20490;
        int mountPort = 20491;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--serve":
                    serve = true;
                    break;
                case "--register":
                    register = true;
                    break;
                case "--nfs-port" when i + 1 < args.Length:
                    nfsPort = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--mount-port" when i + 1 < args.Length:
                    mountPort = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    exportPath ??= args[i];
                    break;
            }
        }

        return new CommandLineOptions(exportPath, serve, register, nfsPort, mountPort);
    }
}
