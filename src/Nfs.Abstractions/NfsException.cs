namespace Nfs.Abstractions;

/// <summary>
/// The exception thrown when an NFS operation fails. It carries the <see cref="NfsStatus"/> that
/// should be reported to the caller, so server handlers can translate failures into protocol
/// replies.
/// </summary>
public sealed class NfsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    public NfsException()
        : this(NfsStatus.ServerFault)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    public NfsException(string message)
        : this(NfsStatus.ServerFault, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public NfsException(string message, Exception innerException)
        : this(NfsStatus.ServerFault, message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    /// <param name="status">The status to report to the caller.</param>
    public NfsException(NfsStatus status)
        : base($"The NFS operation failed with status {status}.") => Status = status;

    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    /// <param name="status">The status to report to the caller.</param>
    /// <param name="message">A message describing the error.</param>
    public NfsException(NfsStatus status, string message)
        : base(message) => Status = status;

    /// <summary>Initializes a new instance of the <see cref="NfsException"/> class.</summary>
    /// <param name="status">The status to report to the caller.</param>
    /// <param name="message">A message describing the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public NfsException(NfsStatus status, string message, Exception innerException)
        : base(message, innerException) => Status = status;

    /// <summary>Gets the NFS status that should be reported to the caller.</summary>
    public NfsStatus Status { get; }
}
