using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.CustomException;

[ExcludeFromCodeCoverage]
public class RequiredParameterNotFoundException : ArgumentException
{
    public RequiredParameterNotFoundException(string name) : base($"A parameter with name '{name}' cannot be null or empty", name)
    {
    }
}