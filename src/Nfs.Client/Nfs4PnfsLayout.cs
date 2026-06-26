using Nfs.Protocol.V4;

namespace Nfs.Client;

/// <summary>A parsed pNFS files layout returned by LAYOUTGET.</summary>
public sealed class Nfs4PnfsLayout
{
    internal Nfs4PnfsLayout(Nfs4LayoutGetResult result, Nfs4Layout layout, Nfs4FileLayout filesLayout)
    {
        Result = result;
        Layout = layout;
        FilesLayout = filesLayout;
    }

    /// <summary>Gets the raw LAYOUTGET result, including the layout state id.</summary>
    public Nfs4LayoutGetResult Result { get; }

    /// <summary>Gets the returned layout segment.</summary>
    public Nfs4Layout Layout { get; }

    /// <summary>Gets the parsed files-layout body.</summary>
    public Nfs4FileLayout FilesLayout { get; }

    /// <summary>Gets whether this layout uses dense file-handle striping.</summary>
    public bool IsDense => (FilesLayout.Util & Nfs4Pnfs.FileLayoutUtilFlagMask) == Nfs4Pnfs.FileLayoutUtilDense;
}
