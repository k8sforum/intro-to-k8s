# Task 6: Update Map Services (GoogleMaps, OpenStreetMaps) to Log Geocoding Retries - Report

**Status:** ✅ COMPLETED

## Summary

Injected ILogger into both GoogleMapsService and OpenStreetMapsService, and added onRetry callbacks to their Polly retry policies to log each retry attempt with attempt number, delay duration, and exception message.

## Changes Made

### 1. GoogleMapsService (`src/common/mytravels.common/Services/GoogleMapsService.cs`)

- **Added import:** `using Microsoft.Extensions.Logging;`
- **Added field:** `private readonly ILogger<GoogleMapsService> _logger;`
- **Updated constructor signature:** 
  - From: `public GoogleMapsService(IConfiguration configuration)`
  - To: `public GoogleMapsService(ILogger<GoogleMapsService> logger, IConfiguration configuration)`
- **Added constructor initialization:** `_logger = logger ?? throw new ArgumentNullException(nameof(logger));`
- **Enhanced retry policy:** Added `onRetry` callback to the `WaitAndRetryAsync` policy that logs retry attempts at Warning level with the format: "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}"

### 2. OpenStreetMapsService (`src/common/mytravels.common/Services/OpenStreetMapsService.cs`)

- **Added import:** `using Microsoft.Extensions.Logging;`
- **Added field:** `private readonly ILogger<OpenStreetMapsService> _logger;`
- **Updated constructor signature:**
  - From: `public OpenStreetMapsService(IConfiguration configuration)`
  - To: `public OpenStreetMapsService(ILogger<OpenStreetMapsService> logger, IConfiguration configuration)`
- **Added constructor initialization:** `_logger = logger ?? throw new ArgumentNullException(nameof(logger));`
- **Enhanced retry policy:** Added `onRetry` callback to the `WaitAndRetryAsync` policy that logs retry attempts at Warning level with the format: "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}"

## Implementation Details

### Retry Callback Implementation

Both services now use the following retry logging pattern:

```csharp
.WaitAndRetryAsync(
    retryCount: _maxRetryAttempts,
    sleepDurationProvider: i => TimeSpan.FromSeconds(Math.Pow(2, i)),
    onRetry: (exception, timespan, retryCount, context) =>
    {
        var exceptionMessage = exception?.Message ?? "timeout";
        _logger.LogWarning(
            "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}",
            retryCount,
            timespan.TotalMilliseconds,
            exceptionMessage
        );
    });
```

Key characteristics:
- Log level: Warning (non-error observation)
- Parameters: Attempt number (1-based), delay in milliseconds, exception message or "timeout"
- Preserved existing retry count (2) and exponential backoff strategy
- Uses structured logging with named placeholders for easy querying

## Verification

### Build Status
✅ Build succeeded with no errors or warnings

```
dotnet build src/common/mytravels.common
  mytravels.contract -> /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/src/common/mytravels.contract/bin/Debug/net10.0/mytravels.contract.dll
  mytravels.common -> /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/src/common/mytravels.common/bin/Debug/net10.0/mytravels.common.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Dependency Injection

The services are registered in `ServiceCollectionExtensions.cs` using:
- `services.AddTransient<IMapsService, OpenStreetMapsService>()`
- `services.AddTransient<IMapsService, GoogleMapsService>()`

The DI container automatically injects `ILogger<T>` as a built-in feature of Microsoft.Extensions.DependencyInjection, so no registration changes are required.

## Deliverables

✅ ILogger<T> injected into both GoogleMapsService and OpenStreetMapsService constructors  
✅ onRetry callback added to both retry policies with proper logging  
✅ Log message format matches specification: "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}"  
✅ Retry callbacks execute on each transient failure before retry  
✅ Build succeeds with no errors  
✅ Changes ready for commit (user-committed)

## Files Modified

- `/Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/src/common/mytravels.common/Services/GoogleMapsService.cs`
- `/Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/src/common/mytravels.common/Services/OpenStreetMapsService.cs`

## Downstream Impact

This task fulfills the requirement for both GoogleMapsService and OpenStreetMapsService in the traceability gaps plan. The retry logging provides visibility into transient geocoding failures and retry behavior, which is essential for troubleshooting and observability in the MyTravels application.
