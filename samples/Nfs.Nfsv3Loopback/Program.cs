using System.Net;
using System.Text;

using Nfs.Client;
using Nfs.Protocol.V3;
using Nfs.Rpc;
using Nfs.Server;

// Build an in-memory file system: /docs/hello.txt
var fileSystem = new InMemoryFileSystem();
var docs = fileSystem.CreateDirectory(fileSystem.Root, "docs");
fileSystem.CreateFile(docs, "hello.txt", Encoding.UTF8.GetBytes("Hello from NFSv3!"));

// Host it as an NFSv3 server on an ephemeral loopback port.
await using var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs3Program(fileSystem));
server.Start();
Console.WriteLine($"NFSv3 server listening on {server.LocalEndPoint}.");

// Drive it with the typed client.
await using var rpc = await RpcClient.ConnectAsync(server.LocalEndPoint);
var nfs = new Nfs3Client(rpc);

await nfs.NullAsync();
Console.WriteLine("NULL  -> ok");

var root = new Nfs3Handle { Data = fileSystem.Root.ToArray() };
var docsLookup = await nfs.LookupAsync(root, "docs");
var fileLookup = await nfs.LookupAsync(docsLookup.Ok.Handle, "hello.txt");
var attributes = await nfs.GetAttributesAsync(fileLookup.Ok.Handle);

Console.WriteLine(
    $"GETATTR docs/hello.txt -> type={attributes.Attributes.Type}, size={attributes.Attributes.Size} bytes");
Console.WriteLine("Done.");
