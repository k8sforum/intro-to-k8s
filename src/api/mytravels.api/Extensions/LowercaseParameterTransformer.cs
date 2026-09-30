namespace mytravels.api.Extensions
{
    /// <summary>
    /// Renders the <c>[controller]</c> route token in lower case so published paths
    /// (and therefore the OpenAPI document) read <c>/api/pointofinterest</c>.
    /// </summary>
    public sealed class LowercaseParameterTransformer : IOutboundParameterTransformer
    {
        public string? TransformOutbound(object? value) => value?.ToString()?.ToLowerInvariant();
    }
}
