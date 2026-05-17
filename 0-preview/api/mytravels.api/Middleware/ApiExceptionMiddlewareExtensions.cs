using System.Diagnostics.CodeAnalysis;

namespace mytravels.api.Middleware
{
    [ExcludeFromCodeCoverage]
    public static class ApiExceptionMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ApiExceptionMiddleware>();
        }
    }
}