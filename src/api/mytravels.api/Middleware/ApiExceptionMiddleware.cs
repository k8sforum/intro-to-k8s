using Newtonsoft.Json;
using System.Diagnostics;
using System.Net;
using mytravels.contract.CustomException;
using mytravels.contract.Dtos;

namespace mytravels.api.Middleware
{
    public class ApiExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiExceptionMiddleware> _logger;

        public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public Func<RequestDelegate, HttpContext, Task> InvokeNext { get; set; } = (next, context) => next(context);

        public Func<HttpResponse, string, Task> WriteResponseAsync { get; set; } = (response, text) => response.WriteAsync(text);

        public async Task Invoke(HttpContext context)
        {
            try
            {
                if (context.Request.Path == "/")
                {
                    await context.Response.WriteAsync("API is running...");
                    return;
                }

                await InvokeNext(_next, context);
            }
            catch (Exception ex) when (ex is RequiredParameterNotFoundException or OutOfRadiusException or InvalidImageException)
            {
                await HandleClientErrorAsync(context, ex, (int)HttpStatusCode.BadRequest);
            }
            catch (Exception ex) when (ex is DataNotFoundException or EntityNotFoundException)
            {
                await HandleClientErrorAsync(context, ex, (int)HttpStatusCode.NotFound);
            }
            catch (ApiException ex)
            {
                await HandleServerErrorAsync(context, ex, ex.StatusCode);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away; there is nobody to respond to and nothing worth logging as an error.
            }
            catch (Exception ex)
            {
                await HandleServerErrorAsync(context, ex, (int)HttpStatusCode.InternalServerError);
            }
        }

        private Task HandleServerErrorAsync(HttpContext context, Exception exception, int httpStatuscode)
        {
            ApiErrorDto error = new()
            {
                Id = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
                HttpStatusCode = httpStatuscode,
                // Raw messages can carry database or infrastructure detail, so only deliberate ApiExceptions are echoed.
                // Everything else is reachable through the trace id in the logs.
                Message = exception is ApiException ? exception.Message : "An unexpected error occurred.",
                Title = "An error occurred in the API.  Please use the id and contact our support team if the error persists.",
                Links = context.Request?.Path ?? ""
            };

            _logger.LogError(exception, "Server error, {ErrorId} -- {ErrorMessage}.", error.Id, exception.Message);
            var result = JsonConvert.SerializeObject(error);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = httpStatuscode;
            return WriteResponseAsync(context.Response, result);
        }

        private Task HandleClientErrorAsync(HttpContext context, Exception exception, int httpStatuscode)
        {
            ApiErrorDto error = new()
            {
                Id = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
                HttpStatusCode = httpStatuscode,
                Message = exception.Message,
                Title = exception.Message,
                Links = context.Request?.Path ?? ""
            };

            _logger.LogWarning(exception, "Client error, {ErrorId} -- {ErrorMessage}.", error.Id, exception.Message);
            var result = JsonConvert.SerializeObject(error);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = httpStatuscode;
            return WriteResponseAsync(context.Response, result);
        }
    }
}