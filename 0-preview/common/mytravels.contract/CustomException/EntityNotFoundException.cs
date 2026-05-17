using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.CustomException;

[ExcludeFromCodeCoverage]
public class EntityNotFoundException : KeyNotFoundException
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}