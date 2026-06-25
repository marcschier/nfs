using Nfs.Xdr;

namespace Nfs.Nsm;

/// <summary>A monitored host name (<c>sm_name</c>).</summary>
[XdrType]
public partial struct Nsm1Name
{
    /// <summary>The monitored host name.</summary>
    [XdrField(0)]
    [XdrString(Nsm1.MaxNameLength)]
    public string MonitorName { get; set; }
}

/// <summary>The caller's NSM callback identity (<c>my_id</c>).</summary>
[XdrType]
public partial struct Nsm1MyId
{
    /// <summary>The caller host name.</summary>
    [XdrField(0)]
    [XdrString(Nsm1.MaxNameLength)]
    public string MyName { get; set; }

    /// <summary>The RPC program number to call back.</summary>
    [XdrField(1)]
    public int Program { get; set; }

    /// <summary>The RPC program version to call back.</summary>
    [XdrField(2)]
    public int Version { get; set; }

    /// <summary>The RPC procedure to call back.</summary>
    [XdrField(3)]
    public int Procedure { get; set; }
}

/// <summary>A monitor identity (<c>mon_id</c>).</summary>
[XdrType]
public partial struct Nsm1MonitorId
{
    /// <summary>The monitored host name.</summary>
    [XdrField(0)]
    [XdrString(Nsm1.MaxNameLength)]
    public string MonitorName { get; set; }

    /// <summary>The caller's callback identity.</summary>
    [XdrField(1)]
    public Nsm1MyId MyId { get; set; }
}

/// <summary>Arguments for <c>SM_MON</c> (<c>mon</c>).</summary>
[XdrType]
public partial struct Nsm1Monitor
{
    /// <summary>The monitor identity.</summary>
    [XdrField(0)]
    public Nsm1MonitorId MonitorId { get; set; }

    /// <summary>A caller-private cookie returned with notifications.</summary>
    [XdrField(1)]
    [XdrFixedOpaque(Nsm1.PrivateLength)]
    public byte[] Private { get; set; }
}

/// <summary>A status-only reply (<c>sm_stat_res</c>).</summary>
[XdrType]
public partial struct Nsm1StatusResult
{
    /// <summary>The operation result.</summary>
    [XdrField(0)]
    public Nsm1Result Result { get; set; }

    /// <summary>The NSM state counter.</summary>
    [XdrField(1)]
    public int State { get; set; }
}

/// <summary>A status value (<c>sm_stat</c>).</summary>
[XdrType]
public partial struct Nsm1Status
{
    /// <summary>The NSM state counter.</summary>
    [XdrField(0)]
    public int State { get; set; }
}

/// <summary>A remote host state-change notification (<c>stat_chge</c>).</summary>
[XdrType]
public partial struct Nsm1StatusChange
{
    /// <summary>The host whose state changed.</summary>
    [XdrField(0)]
    [XdrString(Nsm1.MaxNameLength)]
    public string MonitorName { get; set; }

    /// <summary>The host's new NSM state.</summary>
    [XdrField(1)]
    public int State { get; set; }
}
