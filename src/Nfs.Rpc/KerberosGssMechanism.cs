using System.Runtime.InteropServices;

namespace Nfs.Rpc;

/// <summary>
/// Placeholder for a platform Kerberos GSS mechanism. Real verification requires a KDC and is gated.
/// </summary>
public sealed class KerberosGssMechanism : IGssMechanism
{
    /// <summary>Gets a value indicating whether the current OS has a scaffolded native provider.</summary>
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public IGssContext CreateClientContext(string? targetName = null) =>
        throw CreateNotImplementedException();

    /// <inheritdoc/>
    public IGssContext CreateServerContext() => throw CreateNotImplementedException();

    private static Exception CreateNotImplementedException()
    {
        if (OperatingSystem.IsLinux())
        {
            return new NotImplementedException(
                "MIT Kerberos via libgssapi_krb5 is scaffolded but requires a configured KDC/keytab.");
        }

        if (OperatingSystem.IsWindows())
        {
            return new NotImplementedException(
                "Windows SSPI Kerberos is scaffolded but requires domain/KDC credentials.");
        }

        return new PlatformNotSupportedException("Kerberos GSS is scaffolded only for Linux and Windows.");
    }
}

internal static partial class LinuxGssApiNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GssBufferDesc
    {
        public nuint Length { get; set; }

        public IntPtr Value { get; set; }
    }

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_release_buffer")]
    internal static partial uint GssReleaseBuffer(ref uint minorStatus, ref GssBufferDesc buffer);
}

internal static partial class WindowsSspiNative
{
    [LibraryImport("secur32.dll", EntryPoint = "FreeContextBuffer", SetLastError = true)]
    internal static partial uint FreeContextBuffer(IntPtr contextBuffer);
}
