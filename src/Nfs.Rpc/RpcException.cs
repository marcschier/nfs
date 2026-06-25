namespace Nfs.Rpc;

/// <summary>
/// The exception thrown when an RPC message is malformed or violates the ONC/RPC protocol
/// (RFC 5531) — for example an unexpected message type or an unsupported RPC version.
/// </summary>
public sealed class RpcException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RpcException"/> class.</summary>
    public RpcException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RpcException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    public RpcException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RpcException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public RpcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
