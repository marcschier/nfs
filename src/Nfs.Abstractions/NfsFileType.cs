namespace Nfs.Abstractions;

/// <summary>
/// The type of a file-system object (<c>ftype3</c>, RFC 1813).
/// </summary>
public enum NfsFileType
{
    /// <summary>A regular file (NF3REG).</summary>
    Regular = 1,

    /// <summary>A directory (NF3DIR).</summary>
    Directory = 2,

    /// <summary>A block special device (NF3BLK).</summary>
    BlockDevice = 3,

    /// <summary>A character special device (NF3CHR).</summary>
    CharacterDevice = 4,

    /// <summary>A symbolic link (NF3LNK).</summary>
    SymbolicLink = 5,

    /// <summary>A socket (NF3SOCK).</summary>
    Socket = 6,

    /// <summary>A named pipe / FIFO (NF3FIFO).</summary>
    Fifo = 7,
}
