namespace Nfs.Abstractions;

/// <summary>Evaluates NFSv4 ALLOW and DENY ACEs for a principal.</summary>
public static class NfsAccessControlEvaluator
{
    /// <summary>Computes which requested mask bits are granted and denied by an ACL.</summary>
    /// <param name="entries">The ACL entries, in wire order.</param>
    /// <param name="principal">The requesting principal.</param>
    /// <param name="requested">The requested access mask.</param>
    /// <returns>The explicitly granted and denied masks. Unmatched bits are denied.</returns>
    public static (NfsAceAccessMask Granted, NfsAceAccessMask Denied) Evaluate(
        IEnumerable<NfsAccessControlEntry> entries,
        string principal,
        NfsAceAccessMask requested)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(principal);

        uint remaining = (uint)requested;
        uint granted = 0;
        uint denied = 0;

        foreach (NfsAccessControlEntry entry in entries)
        {
            if (remaining == 0)
            {
                break;
            }

            if (!Matches(entry.Who, principal))
            {
                continue;
            }

            uint applicable = remaining & (uint)entry.AccessMask;
            if (applicable == 0)
            {
                continue;
            }

            if (entry.Type == NfsAceType.Allow)
            {
                granted |= applicable;
                remaining &= ~applicable;
            }
            else if (entry.Type == NfsAceType.Deny)
            {
                denied |= applicable;
                remaining &= ~applicable;
            }
        }

        denied |= remaining;
        return ((NfsAceAccessMask)granted, (NfsAceAccessMask)denied);
    }

    private static bool Matches(string who, string principal) =>
        string.Equals(who, principal, StringComparison.Ordinal) ||
        string.Equals(who, "EVERYONE@", StringComparison.Ordinal);
}
