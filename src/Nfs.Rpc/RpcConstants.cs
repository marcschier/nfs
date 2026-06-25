namespace Nfs.Rpc;

/// <summary>
/// Constants defined by the ONC/RPC protocol (RFC 5531).
/// </summary>
public static class RpcConstants
{
    /// <summary>The RPC protocol version carried in every call (always 2).</summary>
    public const uint RpcVersion = 2;

    /// <summary>The maximum length, in bytes, of an <c>opaque_auth</c> body.</summary>
    public const int MaxAuthBodyLength = 400;

    /// <summary>The maximum length, in bytes, of an AUTH_SYS machine name.</summary>
    public const int MaxMachineNameLength = 255;

    /// <summary>The maximum number of auxiliary group ids carried in an AUTH_SYS credential.</summary>
    public const int MaxAuxiliaryGids = 16;
}
