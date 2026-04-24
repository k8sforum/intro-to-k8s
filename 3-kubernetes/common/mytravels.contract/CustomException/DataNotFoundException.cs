using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.CustomException;

[ExcludeFromCodeCoverage]
public class DataNotFoundException : Exception
{
    public DataNotFoundException(string message) : base(message)
    {
    }
}