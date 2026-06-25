namespace Nfs.Abstractions;

/// <summary>The type of an NFSv4 access control entry.</summary>
public enum NfsAceType : uint
{
    /// <summary>Allow the selected access mask.</summary>
    Allow = 0,

    /// <summary>Deny the selected access mask.</summary>
    Deny = 1,

    /// <summary>Audit the selected access mask.</summary>
    Audit = 2,

    /// <summary>Alarm on the selected access mask.</summary>
    Alarm = 3,
}

/// <summary>NFSv4 access control entry flags.</summary>
[Flags]
public enum NfsAceDescriptor : uint
{
    /// <summary>No ACE flags are set.</summary>
    None = 0,

    /// <summary>Inherit this ACE to files.</summary>
    FileInherit = 0x00000001,

    /// <summary>Inherit this ACE to directories.</summary>
    DirectoryInherit = 0x00000002,

    /// <summary>Do not propagate inheritance beyond direct children.</summary>
    NoPropagateInherit = 0x00000004,

    /// <summary>The ACE is used only for inheritance.</summary>
    InheritOnly = 0x00000008,

    /// <summary>The who field identifies a group.</summary>
    IdentifierGroup = 0x00000040,

    /// <summary>The ACE was inherited.</summary>
    Inherited = 0x00000080,
}

/// <summary>NFSv4 ACE access mask bits.</summary>
[Flags]
public enum NfsAceAccessMask : uint
{
    /// <summary>No access bits are set.</summary>
    None = 0,

    /// <summary>Read file data or list a directory.</summary>
    ReadData = 0x00000001,

    /// <summary>Write file data or add a file to a directory.</summary>
    WriteData = 0x00000002,

    /// <summary>Append file data or add a subdirectory.</summary>
    AppendData = 0x00000004,

    /// <summary>Read named attributes.</summary>
    ReadNamedAttributes = 0x00000008,

    /// <summary>Write named attributes.</summary>
    WriteNamedAttributes = 0x00000010,

    /// <summary>Execute a file or search a directory.</summary>
    Execute = 0x00000020,

    /// <summary>Delete children within a directory.</summary>
    DeleteChild = 0x00000040,

    /// <summary>Read basic attributes.</summary>
    ReadAttributes = 0x00000080,

    /// <summary>Write basic attributes.</summary>
    WriteAttributes = 0x00000100,

    /// <summary>Delete the object.</summary>
    Delete = 0x00010000,

    /// <summary>Read the object's ACL.</summary>
    ReadAcl = 0x00020000,

    /// <summary>Write the object's ACL.</summary>
    WriteAcl = 0x00040000,

    /// <summary>Change the owner.</summary>
    WriteOwner = 0x00080000,

    /// <summary>Use the object for synchronization.</summary>
    Synchronize = 0x00100000,
}

/// <summary>An NFSv4 access control entry (<c>nfsace4</c>).</summary>
/// <param name="Type">The ACE type.</param>
/// <param name="Flags">The ACE flags.</param>
/// <param name="AccessMask">The access mask.</param>
/// <param name="Who">The principal string.</param>
public readonly record struct NfsAccessControlEntry(
    NfsAceType Type,
    NfsAceDescriptor Flags,
    NfsAceAccessMask AccessMask,
    string Who);
