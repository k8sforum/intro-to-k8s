# Task 6: Update Map Services (GoogleMaps, OpenStreetMaps) to Log Geocoding Retries

**Goal:** Inject ILogger into both GoogleMapsService and OpenStreetMapsService, and add `onRetry` callbacks to their Polly retry policies to log each retry attempt.

**Files:**
- Modify: `src/common/mytravels.common/Services/GoogleMapsService.cs`
- Modify: `src/common/mytravels.common/Services/OpenStreetMapsService.cs`

**Interfaces (what this task produces for downstream tasks):**
- Both services accept `ILogger<GoogleMapsService>` / `ILogger<OpenStreetMapsService>` in constructor.
- Retry policies include an `onRetry` callback that logs attempt number, delay, and exception at Warning level.

**Consumes from earlier tasks:**
- No external dependencies; both are modifications to existing services.

**Global Constraints:**
- No new NuGet dependencies; use existing Polly and logging infrastructure.
- Retry count: 3 (match existing `_maxRetryAttempts` convention).
- Log level: Warning (transient retries are not errors, just observations).
- Log format: "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}".

**Steps:**

### Step 1: Update GoogleMapsService constructor

Open `src/common/mytravels.common/Services/GoogleMapsService.cs`.

Add `ILogger<GoogleMapsService>` parameter to the constructor. If the constructor looks like:

```csharp
public GoogleMapsService(...)
{
    // ...
}
```

Update it to:

```csharp
private readonly ILogger<GoogleMapsService> _logger;

public GoogleMapsService(ILogger<GoogleMapsService> logger, ...)
{
    _logger = logger;
    // ... rest of init (preserve existing code)
}
```

### Step 2: Add onRetry callback to GoogleMapsService retry policy

Locate the Polly `WaitAndRetryAsync` policy definition (around line 23-26). It likely looks like:

```csharp
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
    retryCount: ...,
    sleepDurationProvider: ...,
    ...
))
```

Update it to include an `onRetry` parameter:

```csharp
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
    retryCount: 3,
    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
    onRetry: (outcome, timespan, retryCount, context) =>
    {
        _logger.LogWarning(
            "Geocoding retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}",
            retryCount,
            timespan.TotalMilliseconds,
            outcome.Exception?.Message ?? "timeout"
        );
    }
))
```

**Adaptation notes:**
- The exact retry count, sleep duration formula, and other details may differ from the above template. Preserve the existing policy details and only add the `onRetry` callback.
- The `outcome` parameter contains the exception (outcome.Exception).
- The `timespan` is the delay before the next attempt.
- The `retryCount` is the current retry attempt number (1, 2, 3, ...).

### Step 3: Repeat for OpenStreetMapsService

Open `src/common/mytravels.common/Services/OpenStreetMapsService.cs`.

Perform the same two changes as Step 1 and Step 2:
1. Add `ILogger<OpenStreetMapsService>` to constructor.
2. Add `onRetry` callback to the retry policy (around line 25-28).

### Step 4: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/common/mytravels.common
```

Expect: Build succeeds.

### Step 5: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/GoogleMapsService.cs \
         src/common/mytravels.common/Services/OpenStreetMapsService.cs
git commit -m "feat: add retry logging to GoogleMaps and OpenStreetMaps services"
```

**Definition of Done:**
- ILogger<T> injected into both GoogleMapsService and OpenStreetMapsService constructors.
- onRetry callback added to both retry policies.
- Log message includes attempt number, delay (ms), and exception message.
- Retry callback is called on each transient failure (before retry).
- Build succeeds with no errors.
- Changes committed with message provided above.
