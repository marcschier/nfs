using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Nfs.Rpc;

/// <summary>
/// An in-memory test GSS mechanism for loopback RPCSEC_GSS tests. It is not cryptographically secure.
/// </summary>
public sealed class LoopbackGssMechanism : IGssMechanism
{
    private static readonly byte[] SharedKey = "Nfs.Rpc.LoopbackGssMechanism"u8.ToArray();
    private static readonly byte[] ClientToken = "loopback-client-init"u8.ToArray();
    private static readonly byte[] ServerToken = "loopback-server-accept"u8.ToArray();

    /// <inheritdoc/>
    public IGssContext CreateClientContext(string? targetName = null) => new LoopbackGssContext(isInitiator: true);

    /// <inheritdoc/>
    public IGssContext CreateServerContext() => new LoopbackGssContext(isInitiator: false);

    private sealed class LoopbackGssContext : IGssContext
    {
        private readonly bool _isInitiator;

        public LoopbackGssContext(bool isInitiator)
        {
            _isInitiator = isInitiator;
        }

        public bool IsEstablished { get; private set; }

        public GssTokenResult Init(ReadOnlySpan<byte> inputToken)
        {
            if (!_isInitiator)
            {
                throw new InvalidOperationException("Only initiator contexts can produce init tokens.");
            }

            if (inputToken.IsEmpty)
            {
                return new GssTokenResult(ClientToken, GssMajorStatus.ContinueNeeded, 0);
            }

            if (!inputToken.SequenceEqual(ServerToken))
            {
                throw new RpcException("Invalid loopback GSS accept token.");
            }

            IsEstablished = true;
            return new GssTokenResult(ReadOnlyMemory<byte>.Empty, GssMajorStatus.Complete, 0);
        }

        public GssTokenResult Accept(ReadOnlySpan<byte> inputToken)
        {
            if (_isInitiator)
            {
                throw new InvalidOperationException("Only acceptor contexts can accept init tokens.");
            }

            if (!inputToken.SequenceEqual(ClientToken))
            {
                throw new RpcException("Invalid loopback GSS init token.");
            }

            IsEstablished = true;
            return new GssTokenResult(ServerToken, GssMajorStatus.Complete, 0);
        }

        public byte[] GetMic(ReadOnlySpan<byte> message)
        {
            byte[] input = new byte[SharedKey.Length + sizeof(int) + message.Length];
            SharedKey.CopyTo(input, 0);
            BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(SharedKey.Length, sizeof(int)), message.Length);
            message.CopyTo(input.AsSpan(SharedKey.Length + sizeof(int)));
            return SHA256.HashData(input);
        }

        public bool VerifyMic(ReadOnlySpan<byte> message, ReadOnlySpan<byte> mic)
        {
            byte[] expected = GetMic(message);
            return CryptographicOperations.FixedTimeEquals(expected, mic);
        }

        public byte[] Wrap(ReadOnlySpan<byte> message)
        {
            byte[] result = message.ToArray();
            for (int i = 0; i < result.Length; i++)
            {
                result[i] ^= SharedKey[i % SharedKey.Length];
            }

            return result;
        }

        public byte[] Unwrap(ReadOnlySpan<byte> message) => Wrap(message);
    }
}
