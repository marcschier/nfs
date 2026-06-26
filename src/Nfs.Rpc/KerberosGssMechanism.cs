using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nfs.Rpc;

/// <summary>
/// Kerberos V5 GSS mechanism for RPCSEC_GSS. Linux uses MIT Kerberos libgssapi_krb5 with
/// host-based service names such as <c>nfs@localhost</c>, which maps to the
/// <c>nfs/localhost@REALM</c> service principal in the configured keytab.
/// </summary>
public sealed class KerberosGssMechanism : IGssMechanism
{
    private const string DefaultServiceName = "nfs@localhost";

    /// <summary>Gets a value indicating whether the current OS has the implemented Kerberos provider.</summary>
    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <inheritdoc/>
    public IGssContext CreateClientContext(string? targetName = null) =>
        OperatingSystem.IsLinux()
            ? new LinuxGssContext(isInitiator: true, targetName ?? DefaultServiceName)
            : throw CreateUnsupportedException();

    /// <inheritdoc/>
    public IGssContext CreateServerContext() =>
        OperatingSystem.IsLinux()
            ? new LinuxGssContext(isInitiator: false, Environment.GetEnvironmentVariable("NFS_KRB5_SERVICE_NAME") ?? DefaultServiceName)
            : throw CreateUnsupportedException();

    private static PlatformNotSupportedException CreateUnsupportedException()
    {
        if (OperatingSystem.IsWindows())
        {
            return new PlatformNotSupportedException(
                "Windows SSPI Kerberos support is not implemented yet; use Linux libgssapi_krb5 for real RPCSEC_GSS Kerberos.");
        }

        return new PlatformNotSupportedException("Kerberos GSS is supported only on Linux in this build.");
    }

    private sealed class LinuxGssContext : IGssContext, IDisposable
    {
        private const uint GssSComplete = 0;
        private const uint GssSContinueNeeded = 1;
        private const uint GssCIndefinite = 0;
        private const uint GssCQopDefault = 0;
        private const uint GssCMutualFlag = 2;
        private const uint GssCConfFlag = 16;
        private const uint GssCIntegFlag = 32;
        private const uint RequestedFlags = GssCMutualFlag | GssCConfFlag | GssCIntegFlag;
        private const int GssCAccept = 2;
        private const int ConfidentialityRequested = 1;

        private static ReadOnlySpan<byte> HostBasedServiceOid =>
        [
            0x2a, 0x86, 0x48, 0x86, 0xf7, 0x12, 0x01, 0x02, 0x01, 0x04,
        ];

        private readonly bool _isInitiator;
        private IntPtr _context;
        private IntPtr _credential;
        private IntPtr _name;
        private bool _disposed;

        public LinuxGssContext(bool isInitiator, string serviceName)
        {
            _isInitiator = isInitiator;
            _name = ImportHostBasedServiceName(serviceName);
            if (!isInitiator)
            {
                _credential = AcquireAcceptorCredential(_name);
            }
        }

        ~LinuxGssContext()
        {
            ReleaseUnmanaged();
        }

        public bool IsEstablished { get; private set; }

        public GssTokenResult Init(ReadOnlySpan<byte> inputToken)
        {
            ThrowIfDisposed();
            if (!_isInitiator)
            {
                throw new InvalidOperationException("Only initiator contexts can produce init tokens.");
            }

            unsafe
            {
                fixed (byte* input = inputToken)
                {
                    var inputBuffer = CreateInputBuffer(inputToken, input);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssInitSecContext(
                        ref minorStatus,
                        IntPtr.Zero,
                        ref _context,
                        _name,
                        IntPtr.Zero,
                        RequestedFlags,
                        GssCIndefinite,
                        IntPtr.Zero,
                        ref inputBuffer,
                        out _,
                        out LinuxGssApiNative.GssBufferDesc outputBuffer,
                        out _,
                        out _);
                    byte[] outputToken = CopyAndReleaseBuffer(ref outputBuffer);
                    return CreateTokenResult(majorStatus, minorStatus, outputToken);
                }
            }
        }

        public GssTokenResult Accept(ReadOnlySpan<byte> inputToken)
        {
            ThrowIfDisposed();
            if (_isInitiator)
            {
                throw new InvalidOperationException("Only acceptor contexts can accept init tokens.");
            }

            unsafe
            {
                fixed (byte* input = inputToken)
                {
                    var inputBuffer = CreateInputBuffer(inputToken, input);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssAcceptSecContext(
                        ref minorStatus,
                        ref _context,
                        _credential,
                        ref inputBuffer,
                        IntPtr.Zero,
                        out IntPtr sourceName,
                        out _,
                        out LinuxGssApiNative.GssBufferDesc outputBuffer,
                        out _,
                        out _,
                        out IntPtr delegatedCredential);
                    ReleaseName(sourceName);
                    ReleaseCredential(delegatedCredential);
                    byte[] outputToken = CopyAndReleaseBuffer(ref outputBuffer);
                    return CreateTokenResult(majorStatus, minorStatus, outputToken);
                }
            }
        }

        public byte[] GetMic(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            unsafe
            {
                fixed (byte* input = message)
                {
                    var messageBuffer = CreateInputBuffer(message, input);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssGetMic(
                        ref minorStatus,
                        _context,
                        GssCQopDefault,
                        ref messageBuffer,
                        out LinuxGssApiNative.GssBufferDesc micBuffer);
                    ThrowIfError(majorStatus, minorStatus, "gss_get_mic");
                    return CopyAndReleaseBuffer(ref micBuffer);
                }
            }
        }

        public bool VerifyMic(ReadOnlySpan<byte> message, ReadOnlySpan<byte> mic)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            unsafe
            {
                fixed (byte* messagePointer = message)
                fixed (byte* micPointer = mic)
                {
                    var messageBuffer = CreateInputBuffer(message, messagePointer);
                    var micBuffer = CreateInputBuffer(mic, micPointer);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssVerifyMic(
                        ref minorStatus,
                        _context,
                        ref messageBuffer,
                        ref micBuffer,
                        out _);
                    return majorStatus == GssSComplete;
                }
            }
        }

        public byte[] Wrap(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            unsafe
            {
                fixed (byte* input = message)
                {
                    var inputBuffer = CreateInputBuffer(message, input);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssWrap(
                        ref minorStatus,
                        _context,
                        ConfidentialityRequested,
                        GssCQopDefault,
                        ref inputBuffer,
                        out _,
                        out LinuxGssApiNative.GssBufferDesc outputBuffer);
                    ThrowIfError(majorStatus, minorStatus, "gss_wrap");
                    return CopyAndReleaseBuffer(ref outputBuffer);
                }
            }
        }

        public byte[] Unwrap(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            unsafe
            {
                fixed (byte* input = message)
                {
                    var inputBuffer = CreateInputBuffer(message, input);
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssUnwrap(
                        ref minorStatus,
                        _context,
                        ref inputBuffer,
                        out LinuxGssApiNative.GssBufferDesc outputBuffer,
                        out _,
                        out _);
                    ThrowIfError(majorStatus, minorStatus, "gss_unwrap");
                    return CopyAndReleaseBuffer(ref outputBuffer);
                }
            }
        }

        public void Dispose()
        {
            ReleaseUnmanaged();
            GC.SuppressFinalize(this);
        }

        private static IntPtr ImportHostBasedServiceName(string serviceName)
        {
            byte[] serviceNameBytes = Encoding.UTF8.GetBytes(serviceName);
            unsafe
            {
                fixed (byte* namePointer = serviceNameBytes)
                fixed (byte* oidPointer = HostBasedServiceOid)
                {
                    var nameBuffer = new LinuxGssApiNative.GssBufferDesc
                    {
                        Length = (nuint)serviceNameBytes.Length,
                        Value = (IntPtr)namePointer,
                    };
                    var oid = new LinuxGssApiNative.GssOidDesc
                    {
                        Length = (uint)HostBasedServiceOid.Length,
                        Elements = (IntPtr)oidPointer,
                    };
                    uint minorStatus = 0;
                    uint majorStatus = LinuxGssApiNative.GssImportName(
                        ref minorStatus,
                        ref nameBuffer,
                        ref oid,
                        out IntPtr name);
                    ThrowIfError(majorStatus, minorStatus, "gss_import_name");
                    return name;
                }
            }
        }

        private static IntPtr AcquireAcceptorCredential(IntPtr name)
        {
            uint minorStatus = 0;
            uint majorStatus = LinuxGssApiNative.GssAcquireCred(
                ref minorStatus,
                name,
                GssCIndefinite,
                IntPtr.Zero,
                GssCAccept,
                out IntPtr credential,
                out _,
                out _);
            ThrowIfError(majorStatus, minorStatus, "gss_acquire_cred");
            return credential;
        }

        private static unsafe LinuxGssApiNative.GssBufferDesc CreateInputBuffer(
            ReadOnlySpan<byte> inputToken,
            byte* input) =>
            new()
            {
                Length = (nuint)inputToken.Length,
                Value = inputToken.IsEmpty ? IntPtr.Zero : (IntPtr)input,
            };

        private static byte[] CopyAndReleaseBuffer(ref LinuxGssApiNative.GssBufferDesc buffer)
        {
            try
            {
                if (buffer.Length == 0 || buffer.Value == IntPtr.Zero)
                {
                    return [];
                }

                byte[] result = new byte[checked((int)buffer.Length)];
                Marshal.Copy(buffer.Value, result, 0, result.Length);
                return result;
            }
            finally
            {
                ReleaseBuffer(ref buffer);
            }
        }

        private GssTokenResult CreateTokenResult(uint majorStatus, uint minorStatus, byte[] outputToken)
        {
            if (majorStatus == GssSComplete)
            {
                IsEstablished = true;
                return new GssTokenResult(outputToken, GssMajorStatus.Complete, minorStatus);
            }

            if ((majorStatus & GssSContinueNeeded) != 0)
            {
                return new GssTokenResult(outputToken, GssMajorStatus.ContinueNeeded, minorStatus);
            }

            ThrowIfError(majorStatus, minorStatus, _isInitiator ? "gss_init_sec_context" : "gss_accept_sec_context");
            throw new UnreachableException();
        }

        private static void ThrowIfError(uint majorStatus, uint minorStatus, string operation)
        {
            if (majorStatus != GssSComplete)
            {
                throw new RpcException($"{operation} failed with GSS major status 0x{majorStatus:x8} and minor status 0x{minorStatus:x8}.");
            }
        }

        private static void ReleaseBuffer(ref LinuxGssApiNative.GssBufferDesc buffer)
        {
            if (buffer.Value != IntPtr.Zero)
            {
                uint minorStatus = 0;
                _ = LinuxGssApiNative.GssReleaseBuffer(ref minorStatus, ref buffer);
            }
        }

        private static void ReleaseName(IntPtr name)
        {
            if (name != IntPtr.Zero)
            {
                uint minorStatus = 0;
                _ = LinuxGssApiNative.GssReleaseName(ref minorStatus, ref name);
            }
        }

        private static void ReleaseCredential(IntPtr credential)
        {
            if (credential != IntPtr.Zero)
            {
                uint minorStatus = 0;
                _ = LinuxGssApiNative.GssReleaseCred(ref minorStatus, ref credential);
            }
        }

        private void EnsureEstablished()
        {
            if (!IsEstablished)
            {
                throw new InvalidOperationException("The GSS context is not established.");
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private void ReleaseUnmanaged()
        {
            if (_disposed)
            {
                return;
            }

            if (_context != IntPtr.Zero)
            {
                uint minorStatus = 0;
                _ = LinuxGssApiNative.GssDeleteSecContext(ref minorStatus, ref _context, out LinuxGssApiNative.GssBufferDesc outputToken);
                ReleaseBuffer(ref outputToken);
                _context = IntPtr.Zero;
            }

            ReleaseCredential(_credential);
            _credential = IntPtr.Zero;
            ReleaseName(_name);
            _name = IntPtr.Zero;
            _disposed = true;
        }
    }
}

internal static partial class LinuxGssApiNative
{
#pragma warning disable CA1051 // Blittable native interop structs require fields.
    [StructLayout(LayoutKind.Sequential)]
    internal struct GssBufferDesc
    {
        public nuint Length;

        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GssOidDesc
    {
        public uint Length;

        public IntPtr Elements;
    }
#pragma warning restore CA1051

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_import_name")]
    internal static partial uint GssImportName(
        ref uint minorStatus,
        ref GssBufferDesc inputNameBuffer,
        ref GssOidDesc inputNameType,
        out IntPtr outputName);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_acquire_cred")]
    internal static partial uint GssAcquireCred(
        ref uint minorStatus,
        IntPtr desiredName,
        uint timeReq,
        IntPtr desiredMechanisms,
        int credentialUsage,
        out IntPtr outputCredential,
        out IntPtr actualMechanisms,
        out uint timeRec);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_init_sec_context")]
    internal static partial uint GssInitSecContext(
        ref uint minorStatus,
        IntPtr claimantCredential,
        ref IntPtr context,
        IntPtr targetName,
        IntPtr mechanismType,
        uint reqFlags,
        uint timeReq,
        IntPtr inputChannelBindings,
        ref GssBufferDesc inputToken,
        out IntPtr actualMechanismType,
        out GssBufferDesc outputToken,
        out uint returnFlags,
        out uint timeRec);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_accept_sec_context")]
    internal static partial uint GssAcceptSecContext(
        ref uint minorStatus,
        ref IntPtr context,
        IntPtr acceptorCredential,
        ref GssBufferDesc inputToken,
        IntPtr inputChannelBindings,
        out IntPtr sourceName,
        out IntPtr mechanismType,
        out GssBufferDesc outputToken,
        out uint returnFlags,
        out uint timeRec,
        out IntPtr delegatedCredential);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_get_mic")]
    internal static partial uint GssGetMic(
        ref uint minorStatus,
        IntPtr context,
        uint qopRequest,
        ref GssBufferDesc messageBuffer,
        out GssBufferDesc messageToken);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_verify_mic")]
    internal static partial uint GssVerifyMic(
        ref uint minorStatus,
        IntPtr context,
        ref GssBufferDesc messageBuffer,
        ref GssBufferDesc tokenBuffer,
        out uint qopState);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_wrap")]
    internal static partial uint GssWrap(
        ref uint minorStatus,
        IntPtr context,
        int confidentialityRequested,
        uint qopRequest,
        ref GssBufferDesc inputMessageBuffer,
        out int confidentialityState,
        out GssBufferDesc outputMessageBuffer);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_unwrap")]
    internal static partial uint GssUnwrap(
        ref uint minorStatus,
        IntPtr context,
        ref GssBufferDesc inputMessageBuffer,
        out GssBufferDesc outputMessageBuffer,
        out int confidentialityState,
        out uint qopState);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_release_buffer")]
    internal static partial uint GssReleaseBuffer(ref uint minorStatus, ref GssBufferDesc buffer);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_release_name")]
    internal static partial uint GssReleaseName(ref uint minorStatus, ref IntPtr name);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_release_cred")]
    internal static partial uint GssReleaseCred(ref uint minorStatus, ref IntPtr credential);

    [LibraryImport("libgssapi_krb5", EntryPoint = "gss_delete_sec_context")]
    internal static partial uint GssDeleteSecContext(
        ref uint minorStatus,
        ref IntPtr context,
        out GssBufferDesc outputToken);
}

internal static partial class WindowsSspiNative
{
    [LibraryImport("secur32.dll", EntryPoint = "FreeContextBuffer", SetLastError = true)]
    internal static partial uint FreeContextBuffer(IntPtr contextBuffer);
}
