namespace Idora;

/// <summary>
/// The base class for every exception this package throws.
/// </summary>
/// <remarks>
/// Catch this type if you want to handle any misuse of the identifier generator uniformly; catch
/// one of the derived types if you care about the specific problem. The derived types are
/// <see cref="InvalidEntityPrefixException"/>, <see cref="DuplicateEntityPrefixException"/> and
/// <see cref="EntityPrefixNotRegisteredException"/>.
/// </remarks>
public abstract class IdoraException : Exception
{
    /// <summary>Initialises a new instance with a default message.</summary>
    protected IdoraException()
    {
    }

    /// <summary>Initialises a new instance with the specified message.</summary>
    /// <param name="message">A description of the problem.</param>
    protected IdoraException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the specified message and underlying cause.</summary>
    /// <param name="message">A description of the problem.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    protected IdoraException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
