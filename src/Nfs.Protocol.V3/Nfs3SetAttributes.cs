using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>How a SETATTR or create operation should set a timestamp (<c>time_how</c>, RFC 1813).</summary>
public enum Nfs3TimeHow
{
    /// <summary>Leave the time unchanged (DONT_CHANGE).</summary>
    DontChange = 0,

    /// <summary>Set the time to the server's current time (SET_TO_SERVER_TIME).</summary>
    SetToServerTime = 1,

    /// <summary>Set the time to a client-supplied value (SET_TO_CLIENT_TIME).</summary>
    SetToClientTime = 2,
}

/// <summary>
/// Settable file attributes (<c>sattr3</c>, RFC 1813). Each value is optional: an unset numeric
/// field is left unchanged, and timestamps follow <see cref="Nfs3TimeHow"/>.
/// </summary>
public record struct Nfs3SetAttributes : IXdrSerializable<Nfs3SetAttributes>
{
    /// <summary>The new mode bits, or <see langword="null"/> to leave them unchanged.</summary>
    public uint? Mode { get; set; }

    /// <summary>The new owner user id, or <see langword="null"/> to leave it unchanged.</summary>
    public uint? Uid { get; set; }

    /// <summary>The new owner group id, or <see langword="null"/> to leave it unchanged.</summary>
    public uint? Gid { get; set; }

    /// <summary>The new size, or <see langword="null"/> to leave it unchanged.</summary>
    public ulong? Size { get; set; }

    /// <summary>How to set the access time.</summary>
    public Nfs3TimeHow AccessTimeHow { get; set; }

    /// <summary>The access time, used only when <see cref="AccessTimeHow"/> is client-supplied.</summary>
    public Nfs3Time AccessTime { get; set; }

    /// <summary>How to set the modification time.</summary>
    public Nfs3TimeHow ModifyTimeHow { get; set; }

    /// <summary>The modification time, used only when <see cref="ModifyTimeHow"/> is client-supplied.</summary>
    public Nfs3Time ModifyTime { get; set; }

    /// <summary>Gets a value that changes nothing.</summary>
    public static Nfs3SetAttributes None => default;

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        WriteOptionalUInt32(ref writer, Mode);
        WriteOptionalUInt32(ref writer, Uid);
        WriteOptionalUInt32(ref writer, Gid);
        WriteOptionalUInt64(ref writer, Size);

        writer.WriteInt32((int)AccessTimeHow);
        if (AccessTimeHow == Nfs3TimeHow.SetToClientTime)
        {
            AccessTime.WriteTo(ref writer);
        }

        writer.WriteInt32((int)ModifyTimeHow);
        if (ModifyTimeHow == Nfs3TimeHow.SetToClientTime)
        {
            ModifyTime.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3SetAttributes ReadFrom(ref XdrReader reader)
    {
        var result = new Nfs3SetAttributes
        {
            Mode = ReadOptionalUInt32(ref reader),
            Uid = ReadOptionalUInt32(ref reader),
            Gid = ReadOptionalUInt32(ref reader),
            Size = ReadOptionalUInt64(ref reader),
        };

        result.AccessTimeHow = (Nfs3TimeHow)reader.ReadInt32();
        if (result.AccessTimeHow == Nfs3TimeHow.SetToClientTime)
        {
            result.AccessTime = Nfs3Time.ReadFrom(ref reader);
        }

        result.ModifyTimeHow = (Nfs3TimeHow)reader.ReadInt32();
        if (result.ModifyTimeHow == Nfs3TimeHow.SetToClientTime)
        {
            result.ModifyTime = Nfs3Time.ReadFrom(ref reader);
        }

        return result;
    }

    private static void WriteOptionalUInt32(ref XdrWriter writer, uint? value)
    {
        if (value.HasValue)
        {
            writer.WriteBool(true);
            writer.WriteUInt32(value.Value);
        }
        else
        {
            writer.WriteBool(false);
        }
    }

    private static void WriteOptionalUInt64(ref XdrWriter writer, ulong? value)
    {
        if (value.HasValue)
        {
            writer.WriteBool(true);
            writer.WriteUInt64(value.Value);
        }
        else
        {
            writer.WriteBool(false);
        }
    }

    private static uint? ReadOptionalUInt32(ref XdrReader reader) =>
        reader.ReadBool() ? reader.ReadUInt32() : null;

    private static ulong? ReadOptionalUInt64(ref XdrReader reader) =>
        reader.ReadBool() ? reader.ReadUInt64() : null;
}
