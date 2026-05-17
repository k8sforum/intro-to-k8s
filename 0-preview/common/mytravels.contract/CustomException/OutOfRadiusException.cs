using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.CustomException;

[ExcludeFromCodeCoverage]
public class OutOfRadiusException : Exception
{
    public OutOfRadiusException(string message) : base(message)
    {
    }
}