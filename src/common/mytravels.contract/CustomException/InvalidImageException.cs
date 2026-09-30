using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.CustomException;

/// <summary>
/// Raised when an uploaded image cannot be used as a point of interest, for example because it carries no GPS metadata.
/// Surfaces as a 400 rather than a 500 because the caller can fix it.
/// </summary>
[ExcludeFromCodeCoverage]
public class InvalidImageException : Exception
{
    public InvalidImageException(string message) : base(message)
    {
    }
}
