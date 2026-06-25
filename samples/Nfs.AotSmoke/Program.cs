using Nfs.Rpc;
using Nfs.Xdr;

Console.WriteLine($"Nfs AOT smoke OK — XDR block size = {XdrConstants.BlockSize} bytes, "
    + $"RPC record header = {RecordMarking.HeaderSize} bytes, "
    + $"default auth flavor = {OpaqueAuth.None.Flavor}.");
