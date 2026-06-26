using System.Reflection;

namespace Nfs;

/// <summary>
/// Marker for the <c>Nfs</c> umbrella package, which bundles the entire NFS client and server stack
/// — ONC/RPC, XDR, NFS v2/v3/v4.0/4.1/4.2, MOUNT, and NLM/NSM — into a single self-contained package.
/// Reference this package to get the whole library; the per-component <c>Nfs.*</c> assemblies are
/// bundled alongside this one.
/// </summary>
public static class NfsLibrary
{
    /// <summary>Gets the informational version of the bundled <c>Nfs</c> library.</summary>
    public static string Version =>
        typeof(NfsLibrary).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(NfsLibrary).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
