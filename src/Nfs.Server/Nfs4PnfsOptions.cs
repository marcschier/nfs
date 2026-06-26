namespace Nfs.Server;

/// <summary>Configures the NFSv4.1 pNFS files-layout device advertised by an <see cref="Nfs4Program"/>.</summary>
public sealed class Nfs4PnfsOptions
{
    /// <summary>The default files-layout stripe unit, in bytes.</summary>
    public const uint DefaultStripeUnit = 65536;

    private readonly string[] _dataServerUniversalAddresses;

    /// <summary>Creates pNFS options for one files-layout device.</summary>
    /// <param name="dataServerUniversalAddresses">The data-server universal addresses in h1.h2.h3.h4.p1.p2 form.</param>
    /// <param name="stripeUnit">The files-layout stripe unit, in bytes.</param>
    /// <param name="stripeCount">The number of stripe-index entries to advertise, or zero for one per data server.</param>
    public Nfs4PnfsOptions(
        IEnumerable<string> dataServerUniversalAddresses,
        uint stripeUnit = DefaultStripeUnit,
        uint stripeCount = 0)
    {
        ArgumentNullException.ThrowIfNull(dataServerUniversalAddresses);

        _dataServerUniversalAddresses = dataServerUniversalAddresses.ToArray();
        if (_dataServerUniversalAddresses.Length == 0)
        {
            throw new ArgumentException("At least one data-server address is required.", nameof(dataServerUniversalAddresses));
        }

        foreach (string universalAddress in _dataServerUniversalAddresses)
        {
            if (string.IsNullOrWhiteSpace(universalAddress))
            {
                throw new ArgumentException("Data-server addresses cannot be empty.", nameof(dataServerUniversalAddresses));
            }
        }

        if (stripeUnit == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stripeUnit), stripeUnit, "The stripe unit must be non-zero.");
        }

        DataServerCount = (uint)_dataServerUniversalAddresses.Length;
        StripeUnit = stripeUnit;
        StripeCount = stripeCount == 0 ? DataServerCount : stripeCount;
    }

    /// <summary>Gets the universal addresses of the data servers.</summary>
    public IReadOnlyList<string> DataServerUniversalAddresses => _dataServerUniversalAddresses;

    /// <summary>Gets the number of data servers in the advertised device.</summary>
    public uint DataServerCount { get; }

    /// <summary>Gets the files-layout stripe unit, in bytes.</summary>
    public uint StripeUnit { get; }

    /// <summary>Gets the number of round-robin stripe-index entries to advertise.</summary>
    public uint StripeCount { get; }
}
