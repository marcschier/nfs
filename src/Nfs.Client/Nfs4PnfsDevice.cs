using System.Net;

using Nfs.Protocol.V4;

namespace Nfs.Client;

/// <summary>A parsed pNFS files-layout device.</summary>
public sealed class Nfs4PnfsDevice
{
    internal Nfs4PnfsDevice(
        ReadOnlyMemory<byte> deviceId,
        Nfs4DeviceAddress deviceAddress,
        Nfs4FileLayoutDataServerAddress filesAddress,
        IReadOnlyList<IPEndPoint> dataServerEndpoints)
    {
        DeviceId = deviceId;
        DeviceAddress = deviceAddress;
        FilesAddress = filesAddress;
        DataServerEndpoints = dataServerEndpoints;
    }

    /// <summary>Gets the device id.</summary>
    public ReadOnlyMemory<byte> DeviceId { get; }

    /// <summary>Gets the raw device address returned by GETDEVICEINFO.</summary>
    public Nfs4DeviceAddress DeviceAddress { get; }

    /// <summary>Gets the parsed files-layout address body.</summary>
    public Nfs4FileLayoutDataServerAddress FilesAddress { get; }

    /// <summary>Gets the first TCP endpoint for each data server in the device.</summary>
    public IReadOnlyList<IPEndPoint> DataServerEndpoints { get; }
}
