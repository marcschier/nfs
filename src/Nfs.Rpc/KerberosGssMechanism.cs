using System.Buffers.Binary;
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
    private const string DefaultLinuxServiceName = "nfs@localhost";
    private const string DefaultWindowsServiceName = "HOST/localhost";
    private const string WindowsPackageName = "Negotiate";

    /// <summary>Gets a value indicating whether the current OS has the implemented Kerberos provider.</summary>
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public IGssContext CreateClientContext(string? targetName = null) =>
        OperatingSystem.IsLinux()
            ? new LinuxGssContext(isInitiator: true, targetName ?? DefaultLinuxServiceName)
            : OperatingSystem.IsWindows()
                ? new WindowsSspiContext(isInitiator: true, targetName ?? DefaultWindowsServiceName)
                : throw CreateUnsupportedException();

    /// <inheritdoc/>
    public IGssContext CreateServerContext() =>
        OperatingSystem.IsLinux()
            ? new LinuxGssContext(isInitiator: false, Environment.GetEnvironmentVariable("NFS_KRB5_SERVICE_NAME") ?? DefaultLinuxServiceName)
            : OperatingSystem.IsWindows()
                ? new WindowsSspiContext(isInitiator: false, WindowsPackageName)
                : throw CreateUnsupportedException();

    private static PlatformNotSupportedException CreateUnsupportedException()
    {
        return new PlatformNotSupportedException("Kerberos GSS is supported only on Linux and Windows in this build.");
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

    private sealed class WindowsSspiContext : IGssContext, IDisposable
    {
        private const int SecEOk = 0;
        private const int SecIContinueNeeded = 0x00090312;
        private const uint SecBufferVersion = 0;
        private const uint SecBufferData = 1;
        private const uint SecBufferToken = 2;
        private const uint SecBufferPadding = 9;
        private const uint SecPkgAttrSizes = 0;
        private const uint SecDataRepNative = 0x00000010;
        private const uint SecQopWrapNoEncrypt = 0x80000001;
        private const uint CredentialUseInbound = 1;
        private const uint CredentialUseOutbound = 2;
        private const uint InitRequestFlags = 0x00000002 | 0x00000010 | 0x00000100 | 0x00010000;
        private const uint AcceptRequestFlags = 0x00000002 | 0x00000010 | 0x00000100 | 0x00020000;
        private const int WrapPrefixLength = 3 * sizeof(int);

        private readonly bool _isInitiator;
        private readonly string _targetName;
        private WindowsSspiNative.SecPkgContextSizes? _sizes;
        private WindowsSspiNative.SecHandle _credential;
        private WindowsSspiNative.SecHandle _context;
        private bool _hasCredential;
        private bool _hasContext;
        private bool _disposed;

        public WindowsSspiContext(bool isInitiator, string targetName)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw CreateUnsupportedException();
            }

            _isInitiator = isInitiator;
            _targetName = targetName;
            // The Windows loopback tests use Negotiate with the current process identity so
            // non-domain machines can fall back to NTLM. AD-registered SPN Kerberos
            // verification is environment-specific and remains out of band.
            AcquireCredential(isInitiator ? CredentialUseOutbound : CredentialUseInbound);
        }

        ~WindowsSspiContext()
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

            return Initialize(inputToken);
        }

        public GssTokenResult Accept(ReadOnlySpan<byte> inputToken)
        {
            ThrowIfDisposed();
            if (_isInitiator)
            {
                throw new InvalidOperationException("Only acceptor contexts can accept init tokens.");
            }

            return AcceptToken(inputToken);
        }

        public byte[] GetMic(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            WindowsSspiNative.SecPkgContextSizes sizes = GetSizes();
            byte[] token = new byte[checked((int)sizes.MaxSignature)];
            unsafe
            {
                fixed (byte* messagePointer = message)
                fixed (byte* tokenPointer = token)
                {
                    WindowsSspiNative.SecBuffer* buffers = stackalloc WindowsSspiNative.SecBuffer[2];
                    buffers[0] = CreateBuffer(SecBufferData, message.Length, messagePointer);
                    buffers[1] = CreateBuffer(SecBufferToken, token.Length, tokenPointer);
                    WindowsSspiNative.SecBufferDesc descriptor = CreateDescriptor(buffers, 2);
                    int status = WindowsSspiNative.MakeSignature(ref _context, 0, ref descriptor, 0);
                    ThrowIfError(status, "MakeSignature");
                    return token.AsSpan(0, checked((int)buffers[1].BufferSize)).ToArray();
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
                    WindowsSspiNative.SecBuffer* buffers = stackalloc WindowsSspiNative.SecBuffer[2];
                    buffers[0] = CreateBuffer(SecBufferData, message.Length, messagePointer);
                    buffers[1] = CreateBuffer(SecBufferToken, mic.Length, micPointer);
                    WindowsSspiNative.SecBufferDesc descriptor = CreateDescriptor(buffers, 2);
                    int status = WindowsSspiNative.VerifySignature(ref _context, ref descriptor, 0, out _);
                    return status == SecEOk;
                }
            }
        }

        public byte[] Wrap(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            WindowsSspiNative.SecPkgContextSizes sizes = GetSizes();
            byte[] token = new byte[checked((int)sizes.SecurityTrailer)];
            byte[] data = message.ToArray();
            byte[] padding = new byte[checked((int)sizes.BlockSize)];
            unsafe
            {
                fixed (byte* tokenPointer = token)
                fixed (byte* dataPointer = data)
                fixed (byte* paddingPointer = padding)
                {
                    WindowsSspiNative.SecBuffer* buffers = stackalloc WindowsSspiNative.SecBuffer[3];
                    buffers[0] = CreateBuffer(SecBufferToken, token.Length, tokenPointer);
                    buffers[1] = CreateBuffer(SecBufferData, data.Length, dataPointer);
                    buffers[2] = CreateBuffer(SecBufferPadding, padding.Length, paddingPointer);
                    WindowsSspiNative.SecBufferDesc descriptor = CreateDescriptor(buffers, 3);
                    int status = WindowsSspiNative.EncryptMessage(ref _context, 0, ref descriptor, 0);
                    ThrowIfError(status, "EncryptMessage");
                    return PackWrappedMessage(token, data, padding, buffers);
                }
            }
        }

        public byte[] Unwrap(ReadOnlySpan<byte> message)
        {
            ThrowIfDisposed();
            EnsureEstablished();

            UnpackWrappedMessage(message, out byte[] token, out byte[] data, out byte[] padding);
            unsafe
            {
                fixed (byte* tokenPointer = token)
                fixed (byte* dataPointer = data)
                fixed (byte* paddingPointer = padding)
                {
                    WindowsSspiNative.SecBuffer* buffers = stackalloc WindowsSspiNative.SecBuffer[3];
                    buffers[0] = CreateBuffer(SecBufferToken, token.Length, tokenPointer);
                    buffers[1] = CreateBuffer(SecBufferData, data.Length, dataPointer);
                    buffers[2] = CreateBuffer(SecBufferPadding, padding.Length, paddingPointer);
                    WindowsSspiNative.SecBufferDesc descriptor = CreateDescriptor(buffers, 3);
                    int status = WindowsSspiNative.DecryptMessage(ref _context, ref descriptor, 0, out uint qop);
                    ThrowIfError(status, "DecryptMessage");
                    if (qop == SecQopWrapNoEncrypt)
                    {
                        throw new RpcException("SSPI unwrap completed without confidentiality.");
                    }

                    return data.AsSpan(0, checked((int)buffers[1].BufferSize)).ToArray();
                }
            }
        }

        public void Dispose()
        {
            ReleaseUnmanaged();
            GC.SuppressFinalize(this);
        }

        private unsafe GssTokenResult Initialize(ReadOnlySpan<byte> inputToken)
        {
            fixed (byte* input = inputToken)
            {
                WindowsSspiNative.SecBufferDesc inputDescriptor = default;
                WindowsSspiNative.SecBuffer* inputBuffer = stackalloc WindowsSspiNative.SecBuffer[1];
                WindowsSspiNative.SecBufferDesc* inputDescriptorPointer = null;
                if (!inputToken.IsEmpty)
                {
                    inputBuffer[0] = CreateBuffer(SecBufferToken, inputToken.Length, input);
                    inputDescriptor = CreateDescriptor(inputBuffer, 1);
                    inputDescriptorPointer = &inputDescriptor;
                }

                WindowsSspiNative.SecHandle currentContext = _context;
                WindowsSspiNative.SecHandle* currentContextPointer = _hasContext ? &currentContext : null;
                WindowsSspiNative.SecBuffer outputBuffer = CreateBuffer(SecBufferToken, 0, null);
                WindowsSspiNative.SecBufferDesc outputDescriptor = CreateDescriptor(&outputBuffer, 1);
                int status = WindowsSspiNative.InitializeSecurityContext(
                    ref _credential,
                    currentContextPointer,
                    _targetName,
                    InitRequestFlags,
                    0,
                    SecDataRepNative,
                    inputDescriptorPointer,
                    0,
                    out WindowsSspiNative.SecHandle newContext,
                    &outputDescriptor,
                    out _,
                    out _);
                _context = newContext;
                _hasContext = true;
                byte[] outputToken = CopyAndFreeContextBuffer(outputBuffer);
                return CreateTokenResult(status, outputToken, "InitializeSecurityContext");
            }
        }

        private unsafe GssTokenResult AcceptToken(ReadOnlySpan<byte> inputToken)
        {
            fixed (byte* input = inputToken)
            {
                WindowsSspiNative.SecBuffer inputBuffer = CreateBuffer(SecBufferToken, inputToken.Length, input);
                WindowsSspiNative.SecBufferDesc inputDescriptor = CreateDescriptor(&inputBuffer, 1);
                WindowsSspiNative.SecHandle currentContext = _context;
                WindowsSspiNative.SecHandle* currentContextPointer = _hasContext ? &currentContext : null;
                WindowsSspiNative.SecBuffer outputBuffer = CreateBuffer(SecBufferToken, 0, null);
                WindowsSspiNative.SecBufferDesc outputDescriptor = CreateDescriptor(&outputBuffer, 1);
                int status = WindowsSspiNative.AcceptSecurityContext(
                    ref _credential,
                    currentContextPointer,
                    ref inputDescriptor,
                    AcceptRequestFlags,
                    SecDataRepNative,
                    out WindowsSspiNative.SecHandle newContext,
                    &outputDescriptor,
                    out _,
                    out _);
                _context = newContext;
                _hasContext = true;
                byte[] outputToken = CopyAndFreeContextBuffer(outputBuffer);
                return CreateTokenResult(status, outputToken, "AcceptSecurityContext");
            }
        }

        private void AcquireCredential(uint credentialUse)
        {
            int status = WindowsSspiNative.AcquireCredentialsHandle(
                null,
                WindowsPackageName,
                credentialUse,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                out _credential,
                out _);
            ThrowIfError(status, "AcquireCredentialsHandle");
            _hasCredential = true;
        }

        private WindowsSspiNative.SecPkgContextSizes GetSizes()
        {
            if (_sizes is { } sizes)
            {
                return sizes;
            }

            int status = WindowsSspiNative.QueryContextAttributes(
                ref _context,
                SecPkgAttrSizes,
                out sizes);
            ThrowIfError(status, "QueryContextAttributes(SECPKG_ATTR_SIZES)");
            _sizes = sizes;
            return sizes;
        }

        private GssTokenResult CreateTokenResult(int status, byte[] outputToken, string operation)
        {
            if (status == SecEOk)
            {
                IsEstablished = true;
                return new GssTokenResult(outputToken, GssMajorStatus.Complete, 0);
            }

            if (status == SecIContinueNeeded)
            {
                return new GssTokenResult(outputToken, GssMajorStatus.ContinueNeeded, 0);
            }

            ThrowIfError(status, operation);
            throw new UnreachableException();
        }

        private static unsafe WindowsSspiNative.SecBufferDesc CreateDescriptor(
            WindowsSspiNative.SecBuffer* buffers,
            uint count) =>
            new()
            {
                Version = SecBufferVersion,
                BufferCount = count,
                Buffers = (IntPtr)buffers,
            };

        private static unsafe WindowsSspiNative.SecBuffer CreateBuffer(uint type, int length, byte* buffer) =>
            new()
            {
                BufferSize = checked((uint)length),
                BufferType = type,
                Buffer = (IntPtr)buffer,
            };

        private static unsafe byte[] PackWrappedMessage(
            byte[] token,
            byte[] data,
            byte[] padding,
            WindowsSspiNative.SecBuffer* buffers)
        {
            int tokenLength = checked((int)buffers[0].BufferSize);
            int dataLength = checked((int)buffers[1].BufferSize);
            int paddingLength = checked((int)buffers[2].BufferSize);
            byte[] result = new byte[checked(WrapPrefixLength + tokenLength + dataLength + paddingLength)];
            Span<byte> span = result;
            BinaryPrimitives.WriteInt32BigEndian(span, tokenLength);
            BinaryPrimitives.WriteInt32BigEndian(span[sizeof(int)..], dataLength);
            BinaryPrimitives.WriteInt32BigEndian(span[(2 * sizeof(int))..], paddingLength);
            token.AsSpan(0, tokenLength).CopyTo(span[WrapPrefixLength..]);
            data.AsSpan(0, dataLength).CopyTo(span[(WrapPrefixLength + tokenLength)..]);
            padding.AsSpan(0, paddingLength).CopyTo(span[(WrapPrefixLength + tokenLength + dataLength)..]);
            return result;
        }

        private static void UnpackWrappedMessage(
            ReadOnlySpan<byte> message,
            out byte[] token,
            out byte[] data,
            out byte[] padding)
        {
            if (message.Length < WrapPrefixLength)
            {
                throw new RpcException("Invalid SSPI wrapped message.");
            }

            int tokenLength = BinaryPrimitives.ReadInt32BigEndian(message);
            int dataLength = BinaryPrimitives.ReadInt32BigEndian(message[sizeof(int)..]);
            int paddingLength = BinaryPrimitives.ReadInt32BigEndian(message[(2 * sizeof(int))..]);
            int payloadLength = checked(tokenLength + dataLength + paddingLength);
            if (tokenLength < 0 || dataLength < 0 || paddingLength < 0 || message.Length != WrapPrefixLength + payloadLength)
            {
                throw new RpcException("Invalid SSPI wrapped message.");
            }

            token = message.Slice(WrapPrefixLength, tokenLength).ToArray();
            data = message.Slice(WrapPrefixLength + tokenLength, dataLength).ToArray();
            padding = message.Slice(WrapPrefixLength + tokenLength + dataLength, paddingLength).ToArray();
        }

        private static byte[] CopyAndFreeContextBuffer(WindowsSspiNative.SecBuffer buffer)
        {
            try
            {
                if (buffer.Buffer == IntPtr.Zero || buffer.BufferSize == 0)
                {
                    return [];
                }

                byte[] result = new byte[checked((int)buffer.BufferSize)];
                Marshal.Copy(buffer.Buffer, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (buffer.Buffer != IntPtr.Zero)
                {
                    _ = WindowsSspiNative.FreeContextBuffer(buffer.Buffer);
                }
            }
        }

        private static void ThrowIfError(int status, string operation)
        {
            if (status != SecEOk)
            {
                throw new RpcException($"{operation} failed with SSPI status 0x{unchecked((uint)status):x8}.");
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

            if (_hasContext)
            {
                _ = WindowsSspiNative.DeleteSecurityContext(ref _context);
                _hasContext = false;
            }

            if (_hasCredential)
            {
                _ = WindowsSspiNative.FreeCredentialsHandle(ref _credential);
                _hasCredential = false;
            }

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

#if NET7_0_OR_GREATER
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
#else
    // .NET Standard has no LibraryImport source generator. These signatures are fully blittable,
    // so classic DllImport marshalling is byte-for-byte equivalent.
    [DllImport("libgssapi_krb5", EntryPoint = "gss_import_name")]
    internal static extern uint GssImportName(
        ref uint minorStatus,
        ref GssBufferDesc inputNameBuffer,
        ref GssOidDesc inputNameType,
        out IntPtr outputName);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_acquire_cred")]
    internal static extern uint GssAcquireCred(
        ref uint minorStatus,
        IntPtr desiredName,
        uint timeReq,
        IntPtr desiredMechanisms,
        int credentialUsage,
        out IntPtr outputCredential,
        out IntPtr actualMechanisms,
        out uint timeRec);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_init_sec_context")]
    internal static extern uint GssInitSecContext(
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

    [DllImport("libgssapi_krb5", EntryPoint = "gss_accept_sec_context")]
    internal static extern uint GssAcceptSecContext(
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

    [DllImport("libgssapi_krb5", EntryPoint = "gss_get_mic")]
    internal static extern uint GssGetMic(
        ref uint minorStatus,
        IntPtr context,
        uint qopRequest,
        ref GssBufferDesc messageBuffer,
        out GssBufferDesc messageToken);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_verify_mic")]
    internal static extern uint GssVerifyMic(
        ref uint minorStatus,
        IntPtr context,
        ref GssBufferDesc messageBuffer,
        ref GssBufferDesc tokenBuffer,
        out uint qopState);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_wrap")]
    internal static extern uint GssWrap(
        ref uint minorStatus,
        IntPtr context,
        int confidentialityRequested,
        uint qopRequest,
        ref GssBufferDesc inputMessageBuffer,
        out int confidentialityState,
        out GssBufferDesc outputMessageBuffer);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_unwrap")]
    internal static extern uint GssUnwrap(
        ref uint minorStatus,
        IntPtr context,
        ref GssBufferDesc inputMessageBuffer,
        out GssBufferDesc outputMessageBuffer,
        out int confidentialityState,
        out uint qopState);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_release_buffer")]
    internal static extern uint GssReleaseBuffer(ref uint minorStatus, ref GssBufferDesc buffer);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_release_name")]
    internal static extern uint GssReleaseName(ref uint minorStatus, ref IntPtr name);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_release_cred")]
    internal static extern uint GssReleaseCred(ref uint minorStatus, ref IntPtr credential);

    [DllImport("libgssapi_krb5", EntryPoint = "gss_delete_sec_context")]
    internal static extern uint GssDeleteSecContext(
        ref uint minorStatus,
        ref IntPtr context,
        out GssBufferDesc outputToken);
#endif
}

internal static partial class WindowsSspiNative
{
#pragma warning disable CA1051 // Blittable native interop structs require fields.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SecHandle
    {
        public IntPtr Lower;

        public IntPtr Upper;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecBuffer
    {
        public uint BufferSize;

        public uint BufferType;

        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecBufferDesc
    {
        public uint Version;

        public uint BufferCount;

        public IntPtr Buffers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SecPkgContextSizes
    {
        public uint MaxToken;

        public uint MaxSignature;

        public uint BlockSize;

        public uint SecurityTrailer;
    }
#pragma warning restore CA1051

#if NET7_0_OR_GREATER
    [LibraryImport("secur32.dll", EntryPoint = "AcquireCredentialsHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int AcquireCredentialsHandle(
        string? principal,
        string package,
        uint credentialUse,
        IntPtr logonId,
        IntPtr authData,
        IntPtr getKeyFunction,
        IntPtr getKeyArgument,
        out SecHandle credential,
        out long expiry);

    [LibraryImport("secur32.dll", EntryPoint = "InitializeSecurityContextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int InitializeSecurityContext(
        ref SecHandle credential,
        SecHandle* context,
        string targetName,
        uint contextRequest,
        uint reserved1,
        uint targetDataRep,
        SecBufferDesc* input,
        uint reserved2,
        out SecHandle newContext,
        SecBufferDesc* output,
        out uint contextAttributes,
        out long expiry);

    [LibraryImport("secur32.dll", EntryPoint = "AcceptSecurityContext")]
    internal static unsafe partial int AcceptSecurityContext(
        ref SecHandle credential,
        SecHandle* context,
        ref SecBufferDesc input,
        uint contextRequest,
        uint targetDataRep,
        out SecHandle newContext,
        SecBufferDesc* output,
        out uint contextAttributes,
        out long expiry);

    [LibraryImport("secur32.dll", EntryPoint = "QueryContextAttributesW")]
    internal static partial int QueryContextAttributes(
        ref SecHandle context,
        uint attribute,
        out SecPkgContextSizes sizes);

    [LibraryImport("secur32.dll", EntryPoint = "MakeSignature")]
    internal static partial int MakeSignature(
        ref SecHandle context,
        uint qop,
        ref SecBufferDesc message,
        uint sequenceNumber);

    [LibraryImport("secur32.dll", EntryPoint = "VerifySignature")]
    internal static partial int VerifySignature(
        ref SecHandle context,
        ref SecBufferDesc message,
        uint sequenceNumber,
        out uint qop);

    [LibraryImport("secur32.dll", EntryPoint = "EncryptMessage")]
    internal static partial int EncryptMessage(
        ref SecHandle context,
        uint qop,
        ref SecBufferDesc message,
        uint sequenceNumber);

    [LibraryImport("secur32.dll", EntryPoint = "DecryptMessage")]
    internal static partial int DecryptMessage(
        ref SecHandle context,
        ref SecBufferDesc message,
        uint sequenceNumber,
        out uint qop);

    [LibraryImport("secur32.dll", EntryPoint = "DeleteSecurityContext")]
    internal static partial int DeleteSecurityContext(ref SecHandle context);

    [LibraryImport("secur32.dll", EntryPoint = "FreeCredentialsHandle")]
    internal static partial int FreeCredentialsHandle(ref SecHandle credential);

    [LibraryImport("secur32.dll", EntryPoint = "FreeContextBuffer", SetLastError = true)]
    internal static partial int FreeContextBuffer(IntPtr contextBuffer);
#else
    // .NET Standard fallback: classic DllImport. CharSet.Unicode mirrors StringMarshalling.Utf16
    // (the *W entry points take UTF-16); the remaining signatures are blittable.
    [DllImport("secur32.dll", EntryPoint = "AcquireCredentialsHandleW", CharSet = CharSet.Unicode)]
    internal static extern int AcquireCredentialsHandle(
        string? principal,
        string package,
        uint credentialUse,
        IntPtr logonId,
        IntPtr authData,
        IntPtr getKeyFunction,
        IntPtr getKeyArgument,
        out SecHandle credential,
        out long expiry);

    [DllImport("secur32.dll", EntryPoint = "InitializeSecurityContextW", CharSet = CharSet.Unicode)]
    internal static extern unsafe int InitializeSecurityContext(
        ref SecHandle credential,
        SecHandle* context,
        string targetName,
        uint contextRequest,
        uint reserved1,
        uint targetDataRep,
        SecBufferDesc* input,
        uint reserved2,
        out SecHandle newContext,
        SecBufferDesc* output,
        out uint contextAttributes,
        out long expiry);

    [DllImport("secur32.dll", EntryPoint = "AcceptSecurityContext")]
    internal static extern unsafe int AcceptSecurityContext(
        ref SecHandle credential,
        SecHandle* context,
        ref SecBufferDesc input,
        uint contextRequest,
        uint targetDataRep,
        out SecHandle newContext,
        SecBufferDesc* output,
        out uint contextAttributes,
        out long expiry);

    [DllImport("secur32.dll", EntryPoint = "QueryContextAttributesW")]
    internal static extern int QueryContextAttributes(
        ref SecHandle context,
        uint attribute,
        out SecPkgContextSizes sizes);

    [DllImport("secur32.dll", EntryPoint = "MakeSignature")]
    internal static extern int MakeSignature(
        ref SecHandle context,
        uint qop,
        ref SecBufferDesc message,
        uint sequenceNumber);

    [DllImport("secur32.dll", EntryPoint = "VerifySignature")]
    internal static extern int VerifySignature(
        ref SecHandle context,
        ref SecBufferDesc message,
        uint sequenceNumber,
        out uint qop);

    [DllImport("secur32.dll", EntryPoint = "EncryptMessage")]
    internal static extern int EncryptMessage(
        ref SecHandle context,
        uint qop,
        ref SecBufferDesc message,
        uint sequenceNumber);

    [DllImport("secur32.dll", EntryPoint = "DecryptMessage")]
    internal static extern int DecryptMessage(
        ref SecHandle context,
        ref SecBufferDesc message,
        uint sequenceNumber,
        out uint qop);

    [DllImport("secur32.dll", EntryPoint = "DeleteSecurityContext")]
    internal static extern int DeleteSecurityContext(ref SecHandle context);

    [DllImport("secur32.dll", EntryPoint = "FreeCredentialsHandle")]
    internal static extern int FreeCredentialsHandle(ref SecHandle credential);

    [DllImport("secur32.dll", EntryPoint = "FreeContextBuffer", SetLastError = true)]
    internal static extern int FreeContextBuffer(IntPtr contextBuffer);
#endif
}
