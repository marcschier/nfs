using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>RPC authentication flavor values used by NFSv4 SECINFO (<c>secinfo4</c>).</summary>
public enum Nfs4SecurityFlavor
{
    /// <summary>No authentication (AUTH_NONE / AUTH_NULL).</summary>
    None = 0,

    /// <summary>Unix-style uid/gid authentication (AUTH_SYS / AUTH_UNIX).</summary>
    Sys = 1,

    /// <summary>RPCSEC_GSS security context authentication.</summary>
    RpcSecGss = 6,
}

/// <summary>RPCSEC_GSS protection service values used by SECINFO.</summary>
public enum Nfs4RpcGssService
{
    /// <summary>Authenticate only.</summary>
    None = 1,

    /// <summary>Authenticate and protect integrity.</summary>
    Integrity = 2,

    /// <summary>Authenticate and protect privacy.</summary>
    Privacy = 3,
}

/// <summary>RPCSEC_GSS mechanism, qop, and service tuple for a SECINFO flavor.</summary>
public readonly record struct Nfs4RpcSecGssInfo(
    byte[] Oid,
    uint QualityOfProtection,
    Nfs4RpcGssService Service) : IXdrSerializable<Nfs4RpcSecGssInfo>
{
    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteOpaqueVariable(Oid);
        writer.WriteUInt32(QualityOfProtection);
        writer.WriteUInt32((uint)Service);
    }

    /// <inheritdoc/>
    public static Nfs4RpcSecGssInfo ReadFrom(ref XdrReader reader) => new(
        reader.ReadOpaqueVariable(1024).ToArray(),
        reader.ReadUInt32(),
        (Nfs4RpcGssService)reader.ReadUInt32());
}

/// <summary>A single SECINFO security flavor entry (<c>secinfo4</c>).</summary>
public readonly record struct Nfs4SecInfo(Nfs4SecurityFlavor Flavor, Nfs4RpcSecGssInfo? RpcSecGss)
    : IXdrSerializable<Nfs4SecInfo>
{
    /// <summary>Creates an AUTH_NONE entry.</summary>
    public static Nfs4SecInfo AuthNone => new(Nfs4SecurityFlavor.None, null);

    /// <summary>Creates an AUTH_SYS entry.</summary>
    public static Nfs4SecInfo AuthSys => new(Nfs4SecurityFlavor.Sys, null);

    /// <summary>Creates an RPCSEC_GSS entry.</summary>
    /// <param name="info">The GSS mechanism tuple.</param>
    /// <returns>The SECINFO entry.</returns>
    public static Nfs4SecInfo RpcGss(Nfs4RpcSecGssInfo info) => new(Nfs4SecurityFlavor.RpcSecGss, info);

    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Flavor);
        if (Flavor == Nfs4SecurityFlavor.RpcSecGss)
        {
            RpcSecGss.GetValueOrDefault().WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs4SecInfo ReadFrom(ref XdrReader reader)
    {
        var flavor = (Nfs4SecurityFlavor)reader.ReadInt32();
        return flavor == Nfs4SecurityFlavor.RpcSecGss
            ? RpcGss(Nfs4RpcSecGssInfo.ReadFrom(ref reader))
            : new Nfs4SecInfo(flavor, null);
    }
}

/// <summary>The selector for SECINFO_NO_NAME.</summary>
public enum Nfs4SecInfoStyle
{
    /// <summary>Return security information for the current file handle.</summary>
    CurrentFileHandle = 0,

    /// <summary>Return security information for the parent of the current file handle.</summary>
    Parent = 1,
}
