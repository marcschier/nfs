using Nfs.Xdr;

namespace Nfs.Mount;

/// <summary>Arguments for MNT (a <c>dirpath</c> string, RFC 1813).</summary>
[XdrType]
public partial struct Mount3MountArgs
{
    /// <summary>The export path to mount.</summary>
    [XdrField(0)]
    [XdrString(Mount3.MaxPathLength)]
    public string Path { get; set; }
}

/// <summary>The success arm of MNT (<c>mountres3_ok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Mount3MountResultOk
{
    /// <summary>The export's root file handle.</summary>
    [XdrField(0)]
    [XdrOpaque(64)]
    public byte[] Handle { get; set; }

    /// <summary>The authentication flavors the server accepts for this export.</summary>
    [XdrField(1)]
    [XdrArray(16)]
    public int[] AuthFlavors { get; set; }
}

/// <summary>The result of MNT (<c>mountres3</c>, RFC 1813), discriminated on the status.</summary>
public record struct Mount3MountResult : IXdrSerializable<Mount3MountResult>
{
    /// <summary>The mount status.</summary>
    public Mount3Status Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="Mount3Status.Ok"/>).</summary>
    public Mount3MountResultOk Ok { get; set; }

    /// <summary>Gets a value indicating whether the mount succeeded.</summary>
    public readonly bool IsSuccess => Status == Mount3Status.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Mount3MountResult Success(Mount3MountResultOk ok) => new() { Status = Mount3Status.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Mount3MountResult Failure(Mount3Status status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == Mount3Status.Ok)
        {
            Ok.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Mount3MountResult ReadFrom(ref XdrReader reader)
    {
        var status = (Mount3Status)reader.ReadInt32();
        return status == Mount3Status.Ok
            ? new Mount3MountResult { Status = status, Ok = Mount3MountResultOk.ReadFrom(ref reader) }
            : new Mount3MountResult { Status = status };
    }
}

/// <summary>A single EXPORT entry containing an exported path and the groups allowed to mount it.</summary>
/// <param name="Directory">The exported directory path.</param>
/// <param name="Groups">The allowed group names; an empty list means unrestricted.</param>
public readonly record struct Mount3ExportEntry(string Directory, string[] Groups);

/// <summary>The EXPORT result list, encoded as the MOUNT <c>exports</c> linked-list type.</summary>
public record struct Mount3ExportList : IXdrSerializable<Mount3ExportList>
{
    /// <summary>The exported directories.</summary>
    public Mount3ExportEntry[] Exports { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        foreach (Mount3ExportEntry export in Exports ?? [])
        {
            writer.WriteBool(true);
            writer.WriteString(export.Directory);
            foreach (string group in export.Groups ?? [])
            {
                writer.WriteBool(true);
                writer.WriteString(group);
            }

            writer.WriteBool(false);
        }

        writer.WriteBool(false);
    }

    /// <inheritdoc/>
    public static Mount3ExportList ReadFrom(ref XdrReader reader)
    {
        var exports = new List<Mount3ExportEntry>();
        while (reader.ReadBool())
        {
            string directory = reader.ReadString(Mount3.MaxPathLength);
            var groups = new List<string>();
            while (reader.ReadBool())
            {
                groups.Add(reader.ReadString(Mount3.MaxNameLength));
            }

            exports.Add(new Mount3ExportEntry(directory, [.. groups]));
        }

        return new Mount3ExportList { Exports = [.. exports] };
    }
}

/// <summary>A single DUMP entry containing a mounted host and directory path.</summary>
/// <param name="Hostname">The client host name.</param>
/// <param name="Directory">The mounted directory path.</param>
public readonly record struct Mount3MountEntry(string Hostname, string Directory);

/// <summary>The DUMP result list, encoded as the MOUNT <c>mountlist</c> linked-list type.</summary>
public record struct Mount3MountList : IXdrSerializable<Mount3MountList>
{
    /// <summary>The active mount entries.</summary>
    public Mount3MountEntry[] Mounts { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        foreach (Mount3MountEntry mount in Mounts ?? [])
        {
            writer.WriteBool(true);
            writer.WriteString(mount.Hostname);
            writer.WriteString(mount.Directory);
        }

        writer.WriteBool(false);
    }

    /// <inheritdoc/>
    public static Mount3MountList ReadFrom(ref XdrReader reader)
    {
        var mounts = new List<Mount3MountEntry>();
        while (reader.ReadBool())
        {
            string hostname = reader.ReadString(Mount3.MaxNameLength);
            string directory = reader.ReadString(Mount3.MaxPathLength);
            mounts.Add(new Mount3MountEntry(hostname, directory));
        }

        return new Mount3MountList { Mounts = [.. mounts] };
    }
}
