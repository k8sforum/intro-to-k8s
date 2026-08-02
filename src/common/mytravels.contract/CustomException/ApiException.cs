using System.Diagnostics.CodeAnalysis;
using mytravels.contract.Dtos;

namespace mytravels.contract.CustomException;

[ExcludeFromCodeCoverage]
public class ApiException : Exception
{
    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public ApiException(ApiErrorDto apiError) : base($"API call [{apiError.Links}] failed, status code [{apiError.HttpStatusCode}], message [{apiError.Message}] ")
    {
        StatusCode = apiError.HttpStatusCode;
        ApiError = apiError;
    }

    public int StatusCode { get; }
    public ApiErrorDto ApiError { get; }

    protected ApiException(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext)
    {
        throw new NotImplementedException();
    }
}