# Traceability Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement distributed trace propagation, bounded-retry failure signaling, structured logging, and `CorrelationId` persistence across the upload-transaction pipeline (api → RabbitMQ → messaging), eliminating infinite requeues, lost traces, and silent message loss.

**Architecture:** 
- W3C `traceparent` propagated via RabbitMQ message headers using existing ASP.NET Core `Activity.Id`.
- Single `ActivitySource("MyTravels.RabbitMQ")` for Publisher (Producer) and Consumer spans in both `api` and `messaging`.
- Bounded retry (3 attempts) with `x-retry-count` header; failed messages published to `-failed` exchanges (fanout) instead of infinite requeue.
- `CorrelationId` persisted in `PointOfInterest` table; used in structured logs and message headers.
- Sweeper batches now per-point try/catch, sweeper retries tied back to original `CorrelationId`.
- Publisher confirms detect silent message loss; geocoding retries logged.

**Tech Stack:** 
- .NET 10, OpenTelemetry (existing), EF Core (migrations), RabbitMQ (BasicProperties, headers, publisher confirms).

**Spec:** `prompts/todo/traceability gaps spec.md`

## Global Constraints

- No new NuGet dependencies (use existing OTel, ASP.NET Core Activity API).
- ActivitySource name: `"MyTravels.RabbitMQ"` (exact).
- Retry threshold: 3 attempts (match existing `GoogleMapsService._maxRetryAttempts`).
- Failed-message exchange naming: `{exchangeName}-failed` (fanout, no bound queue).
- All structured-log additions use existing `.LogError(ex, "message {Field1} {Field2}", val1, val2)` pattern.
- CorrelationId column is nullable `Guid?` (backfill not required for pre-migration rows).
- No RabbitMQ infrastructure changes (no DLX configuration in any stage).
- Publisher confirms enable application-level nack/return logging only (no schema changes).

---

## Task 1: Add Contract Types (FailedMessage, Exchange Constants, CorrelationId Property)

**Files:**
- Modify: `src/contract/mytravels.contract/Constants/ExchangeNames.cs:1-end`
- Create: `src/contract/mytravels.contract/Messages/FailedMessage.cs`
- Modify: `src/contract/mytravels.contract/Entities/PointOfInterest.cs:1-end`

**Interfaces:**
- Produces: `ExchangeNames.{ResizeImageFailed, AppendFormattedAddressFailed, AppendImageTagsFailed}` constants (strings).
- Produces: `FailedMessage` interface implementing `IMessage` with fields `{ Guid CorrelationId; int PointOfInterestId; string OriginalExchange; string ErrorMessage; DateTime FailedAt; }`.
- Produces: `PointOfInterest.CorrelationId` property (`public Guid? CorrelationId { get; set; }`).

- [ ] **Step 1: Add three `-failed` exchange constants to ExchangeNames.cs**

Open `src/contract/mytravels.contract/Constants/ExchangeNames.cs`. Add three new public constants:

```csharp
public const string ResizeImageFailed = "resize-image-failed";
public const string AppendFormattedAddressFailed = "append-formatted-address-failed";
public const string AppendImageTagsFailed = "append-image-tags-failed";
```

- [ ] **Step 2: Create FailedMessage.cs**

Create new file `src/contract/mytravels.contract/Messages/FailedMessage.cs` with:

```csharp
using System;
using MyTravels.Common.Contracts;

namespace MyTravels.Contract.Messages
{
    public class FailedMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public int PointOfInterestId { get; set; }
        public string OriginalExchange { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime FailedAt { get; set; }
    }
}
```

- [ ] **Step 3: Add CorrelationId property to PointOfInterest.cs**

Open `src/contract/mytravels.contract/Entities/PointOfInterest.cs`. Locate the existing properties (e.g., `Description`, `Tags`). Add:

```csharp
public Guid? CorrelationId { get; set; }
```

Place it near the other metadata properties (after `UpdatedAt` or similar, before navigation properties).

- [ ] **Step 4: Commit Task 1**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/contract/mytravels.contract/Constants/ExchangeNames.cs \
         src/contract/mytravels.contract/Messages/FailedMessage.cs \
         src/contract/mytravels.contract/Entities/PointOfInterest.cs
git commit -m "feat: add traceability contract types (FailedMessage, failed exchanges, CorrelationId)"
```

---

## Task 2: Add EF Core Migration for CorrelationId Column

**Files:**
- Create: `src/domain/mytravels.domain/Migrations/AddPointOfInterestCorrelationId.cs`
- Modify: `src/domain/mytravels.domain/StorageContext.cs` (no-op; EF will regenerate snapshot)

**Interfaces:**
- Consumes: `PointOfInterest.CorrelationId` property (from Task 1).
- Produces: Migration that adds nullable `Guid?` column `CorrelationId` to `PointOfInterest` table.

- [ ] **Step 1: Generate the migration via EF Core**

From the `src/domain/` directory, run:

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/src/domain/mytravels.domain
dotnet ef migrations add AddPointOfInterestCorrelationId -p . -s ../../../src/api/mytravels.api
```

EF will generate a migration file in the `Migrations/` folder (e.g., `20260911xxxxxx_AddPointOfInterestCorrelationId.cs`).

- [ ] **Step 2: Verify the generated migration**

Open the generated migration file. Confirm it contains:

- `migrationBuilder.AddColumn<Guid?>("CorrelationId", "PointOfInterest", nullable: true);` in `Up()`.
- `migrationBuilder.DropColumn("CorrelationId", "PointOfInterest");` in `Down()`.

No manual edits needed if EF got it right.

- [ ] **Step 3: Commit Task 2**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/domain/mytravels.domain/Migrations/
git commit -m "feat: add migration for PointOfInterest.CorrelationId column"
```

---

## Task 3: Update MessagePublisher to Propagate Traces, Publisher Confirms, and CorrelationId Headers

**Files:**
- Modify: `src/common/mytravels.common/Services/MessagePublisher.cs:1-end`

**Interfaces:**
- Consumes: `ActivitySource("MyTravels.RabbitMQ")` (will be registered in Task 5 & 6; MessagePublisher just uses it).
- Consumes: Message type (generic `T`), must have `CorrelationId` field.
- Produces: 
  - Trace-context propagation: `Activity.Current?.Id` → `BasicProperties.Headers["traceparent"]`.
  - AMQP correlation: `BasicProperties.CorrelationId` = message's `CorrelationId?.ToString()`.
  - Publisher confirms: `ConfirmSelectAsync()` before publish, await confirm.
  - Warning log on nack/return.

- [ ] **Step 1: Add static ActivitySource to MessagePublisher**

Open `src/common/mytravels.common/Services/MessagePublisher.cs`. At the top of the class, add:

```csharp
private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");
```

(You may need to add `using System.Diagnostics;` if not already present.)

- [ ] **Step 2: Update PublishAsync to create trace Activity, set headers, enable publisher confirms**

Locate the `PublishAsync` method. Replace the entire method body with:

```csharp
public async Task PublishAsync<T>(string exchange, T message) where T : IMessage
{
    using (var activity = _activitySource.StartActivity($"{exchange} publish", ActivityKind.Producer))
    {
        var properties = _channel.CreateBasicProperties();
        properties.DeliveryMode = 2; // persistent
        
        // Trace propagation: attach traceparent header
        if (Activity.Current?.Id != null)
        {
            properties.Headers ??= new Dictionary<string, object>();
            properties.Headers["traceparent"] = Activity.Current.Id;
        }
        
        // AMQP correlation: use message's CorrelationId
        var messageCorrelationId = (Guid?)message.GetType().GetProperty("CorrelationId")?.GetValue(message);
        if (messageCorrelationId != null && messageCorrelationId != Guid.Empty)
        {
            properties.CorrelationId = messageCorrelationId.ToString();
        }
        
        // Publisher confirms: enable confirms and await them
        await _channel.ConfirmSelectAsync();
        
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        
        // Publish and wait for confirmation
        var confirmResult = await _channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: "",
            mandatory: true, // detect unroutable messages
            basicProperties: properties,
            body: body
        );
        
        // If nacked or returned, log a warning
        if (!confirmResult.IsAck || confirmResult.IsReturned)
        {
            _logger.LogWarning(
                "Message publish nacked or unroutable for exchange {Exchange}, correlation {CorrelationId}",
                exchange,
                messageCorrelationId
            );
        }
    }
}
```

**Note:** If the old `PublishAsync` used different variable names or structure, adapt the above to fit. The key additions are:
- `using (var activity = _activitySource.StartActivity(...))` for tracing.
- `properties.Headers["traceparent"] = Activity.Current?.Id`.
- `properties.CorrelationId = messageCorrelationId?.ToString()`.
- `ConfirmSelectAsync()` and await the confirm result.
- Warning log on nack/return.

- [ ] **Step 3: Add using directives if needed**

Ensure these are at the top of the file:
```csharp
using System.Diagnostics;
using System.Collections.Generic;
```

- [ ] **Step 4: Commit Task 3**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/MessagePublisher.cs
git commit -m "feat: add trace propagation, publisher confirms, and correlation headers to MessagePublisher"
```

---

## Task 4: Update MessageSubscriberBase to Extract Traces, Implement Bounded Retry, Publish Failed Messages

**Files:**
- Modify: `src/common/mytravels.common/Services/MessageSubscriberBase.cs:1-end`

**Interfaces:**
- Consumes: 
  - `ActivitySource("MyTravels.RabbitMQ")` (registered in Task 5 & 6).
  - Message headers with optional `traceparent` and `x-retry-count`.
  - `-failed` exchange name (passed via constructor).
- Produces:
  - Consumer `Activity` linked to Publisher via `traceparent` header.
  - Bounded retry: 3 attempts with `x-retry-count` header increment.
  - Failed messages published to `-failed` exchange on threshold.
  - Structured error logs with `PointOfInterestId` and `CorrelationId`.

- [ ] **Step 1: Add static ActivitySource and update constructor**

Open `src/common/mytravels.common/Services/MessageSubscriberBase.cs`. At the top of the class, add:

```csharp
private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");
private readonly string _failedExchangeName;
```

Update the constructor to accept `failedExchangeName` (string parameter). If constructor is `public MessageSubscriberBase<T>(IModel channel, ILogger logger, ...)`, add:

```csharp
public MessageSubscriberBase(
    IModel channel,
    ILogger logger,
    string failedExchangeName,
    ... // existing params
)
{
    _channel = channel;
    _logger = logger;
    _failedExchangeName = failedExchangeName;
    // ... rest of init
}
```

Each derived class (in messaging) will pass its `-failed` exchange name when calling `base(...)`.

- [ ] **Step 2: Update ReceivedAsync to extract trace context and implement bounded retry**

Locate the `ReceivedAsync` method. Replace it with:

```csharp
private async Task ReceivedAsync(object model, BasicDeliverEventArgs ea)
{
    // Extract trace context from headers
    var traceparent = null as string;
    if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("traceparent", out var tp))
    {
        traceparent = tp?.ToString();
    }
    
    // Start linked Consumer Activity
    ActivityContext linkedContext = default;
    if (!string.IsNullOrEmpty(traceparent))
    {
        if (ActivityContext.TryParse(traceparent, null, out linkedContext))
        {
            // successfully parsed
        }
    }
    
    using (var activity = _activitySource.StartActivity(
        $"{ea.Exchange} consume",
        ActivityKind.Consumer,
        linkedContext))
    {
        try
        {
            // Read retry count from headers
            var retryCount = 0;
            if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("x-retry-count", out var rc))
            {
                if (int.TryParse(rc?.ToString(), out var count))
                {
                    retryCount = count;
                }
            }
            
            // Deserialize message
            var message = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(ea.Body.ToArray()));
            
            // Process the message
            await ProcessMessageAsync(message);
            
            // Success: acknowledge
            await _channel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Exchange}, retry count {RetryCount}", ea.Exchange, retryCount);
            
            // Check retry threshold
            if (retryCount < 3)
            {
                // Increment retry count header and requeue
                var properties = _channel.CreateBasicProperties();
                properties.Headers ??= new Dictionary<string, object>();
                properties.Headers["x-retry-count"] = retryCount + 1;
                
                // Copy other headers from original
                if (ea.BasicProperties?.Headers != null)
                {
                    foreach (var kvp in ea.BasicProperties.Headers)
                    {
                        if (kvp.Key != "x-retry-count")
                        {
                            properties.Headers[kvp.Key] = kvp.Value;
                        }
                    }
                }
                
                await _channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true);
                _logger.LogWarning("Message nacked and requeued (attempt {RetryCount}/3) for {Exchange}", retryCount + 1, ea.Exchange);
            }
            else
            {
                // Failed after 3 attempts: publish to -failed exchange
                var failedMsg = new
                {
                    CorrelationId = message.GetType().GetProperty("CorrelationId")?.GetValue(message) as Guid?,
                    PointOfInterestId = message.GetType().GetProperty("PointOfInterestId")?.GetValue(message),
                    OriginalExchange = ea.Exchange,
                    ErrorMessage = ex.Message,
                    FailedAt = DateTime.UtcNow
                };
                
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(failedMsg));
                await _channel.BasicPublishAsync(
                    exchange: _failedExchangeName,
                    routingKey: "",
                    mandatory: false,
                    basicProperties: null,
                    body: body
                );
                
                // Acknowledge original message so it stops retrying
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
                _logger.LogError("Message dead-lettered to {FailedExchange} after 3 attempts, correlation {CorrelationId}",
                    _failedExchangeName,
                    failedMsg.CorrelationId);
            }
        }
    }
}
```

- [ ] **Step 3: Ensure failed exchanges are declared**

In the constructor or initialization, ensure all three `-failed` exchanges are declared (fanout, non-durable is fine since no queue binds to them):

```csharp
// In constructor after channel is set
Task.Run(async () =>
{
    await _channel.ExchangeDeclareAsync(
        exchange: _failedExchangeName,
        type: "fanout",
        durable: false,
        autoDelete: true);
});
```

Or call this once during subscriber registration in `Program.cs` (will be done in Task 5 & 6).

- [ ] **Step 4: Add using directives if needed**

Ensure these are at the top of the file:
```csharp
using System.Diagnostics;
using System.Collections.Generic;
```

- [ ] **Step 5: Commit Task 4**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/MessageSubscriberBase.cs
git commit -m "feat: add trace extraction, bounded retry, and failed-message publishing to MessageSubscriberBase"
```

---

## Task 5: Update CronJobBase to Guard First Run

**Files:**
- Modify: `src/common/mytravels.common/Services/CronJobBase.cs:1-end`

**Interfaces:**
- Produces: Exception on first `DoWorkAsync()` call is caught and logged (not crashed), same as periodic runs.

- [ ] **Step 1: Extract catch handler into private method**

Open `src/common/mytravels.common/Services/CronJobBase.cs`. Locate the existing `try`/`catch` in the periodic loop. Extract the catch body into a private method:

```csharp
private void HandleCronException(Exception ex)
{
    _logger.LogError(ex, "Cron job {JobName} failed", GetType().Name);
}
```

- [ ] **Step 2: Wrap first DoWorkAsync call in try/catch**

Locate the `ExecuteAsync` method. Currently it looks like:

```csharp
public async Task ExecuteAsync(CancellationToken cancellationToken)
{
    await DoWorkAsync();
    
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await DoWorkAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "...");
        }
        // delay...
    }
}
```

Change to:

```csharp
public async Task ExecuteAsync(CancellationToken cancellationToken)
{
    try
    {
        await DoWorkAsync();
    }
    catch (Exception ex)
    {
        HandleCronException(ex);
    }
    
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await DoWorkAsync();
        }
        catch (Exception ex)
        {
            HandleCronException(ex);
        }
        // delay...
    }
}
```

- [ ] **Step 3: Commit Task 5**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/CronJobBase.cs
git commit -m "fix: guard first cron run with exception handling"
```

---

## Task 6: Update Map Services (GoogleMaps, OpenStreetMaps) to Log Geocoding Retries

**Files:**
- Modify: `src/common/mytravels.common/Services/GoogleMapsService.cs:1-end`
- Modify: `src/common/mytravels.common/Services/OpenStreetMapsService.cs:1-end`

**Interfaces:**
- Consumes: `ILogger` injected into constructor.
- Produces: Retry attempt logged at Warning level with attempt number and exception.

- [ ] **Step 1: Add ILogger to GoogleMapsService constructor**

Open `src/common/mytravels.common/Services/GoogleMapsService.cs`. Add `ILogger<GoogleMapsService>` parameter to the constructor (if not already present):

```csharp
private readonly ILogger<GoogleMapsService> _logger;

public GoogleMapsService(ILogger<GoogleMapsService> logger, ...)
{
    _logger = logger;
    // ... rest of init
}
```

- [ ] **Step 2: Add onRetry callback to retry policy in GoogleMapsService**

Locate the `WaitAndRetryAsync` policy definition (around line 23-26). Find the line that looks like:

```csharp
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(...))
```

or similar. Update the retry policy to include an `onRetry` handler:

```csharp
.AddTransientHttpErrorPolicy(p => p
    .WaitAndRetryAsync(
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
    )
)
```

Adjust the `retryCount`, `sleepDurationProvider`, and other details to match the existing policy.

- [ ] **Step 3: Repeat for OpenStreetMapsService**

Open `src/common/mytravels.common/Services/OpenStreetMapsService.cs`. Perform the same changes as Step 1 & 2 above (add `ILogger<OpenStreetMapsService>`, update retry policy with `onRetry` callback).

- [ ] **Step 4: Commit Task 6**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/GoogleMapsService.cs \
         src/common/mytravels.common/Services/OpenStreetMapsService.cs
git commit -m "feat: add retry logging to GoogleMaps and OpenStreetMaps services"
```

---

## Task 7: Update API Program.cs to Register MyTravels.RabbitMQ ActivitySource

**Files:**
- Modify: `src/api/mytravels.api/Program.cs:29-32` (around `.AddSource("Npgsql")`)

**Interfaces:**
- Consumes: Nothing new; just adds the source alongside existing `.AddSource()` calls.
- Produces: `MyTravels.RabbitMQ` traces appear in OTLP export.

- [ ] **Step 1: Add ActivitySource to API tracing**

Open `src/api/mytravels.api/Program.cs`. Find the line where `.AddSource("Npgsql")` is called (around line 29-32). Add the new source right after:

```csharp
.AddSource("MyTravels.RabbitMQ")
```

So the section looks like:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddSource("MyTravels.RabbitMQ")
        // ... rest of config
    );
```

- [ ] **Step 2: Commit Task 7**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/api/mytravels.api/Program.cs
git commit -m "feat: register MyTravels.RabbitMQ ActivitySource in API tracing"
```

---

## Task 8: Update API ApiExceptionMiddleware to Use TraceId as ErrorId

**Files:**
- Modify: `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs:61-70, 79-88`

**Interfaces:**
- Consumes: `Activity.Current?.TraceId`.
- Produces: Returned error response includes `ErrorId = TraceId` (or fallback random Guid if no trace).

- [ ] **Step 1: Update HandleServerErrorAsync method**

Open `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs`. Locate the line in `HandleServerErrorAsync` where `ErrorId` is assigned (around line 61). Change:

```csharp
Id = Guid.NewGuid().ToString("N")
```

to:

```csharp
Id = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
```

- [ ] **Step 2: Update HandleClientErrorAsync method**

Locate the similar line in `HandleClientErrorAsync` (around line 79). Apply the same change.

- [ ] **Step 3: Add using directive if needed**

Ensure `using System.Diagnostics;` is at the top of the file.

- [ ] **Step 4: Commit Task 8**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs
git commit -m "feat: link API error IDs to trace IDs for easier support lookup"
```

---

## Task 9: Update API PointOfInterestService to Persist CorrelationId

**Files:**
- Modify: `src/api/mytravels.api/Services/PointOfInterestService.cs:84-119`

**Interfaces:**
- Consumes: 
  - `PointOfInterest.CorrelationId` property (from Task 1).
  - Message `CorrelationId` field (from Task 1).
- Produces: 
  - `CreatePointOfInterestAsync` persists `CorrelationId` to entity before insert.
  - `UpdatePointOfInterestAsync` reuses existing `point.CorrelationId` on resize-image publish.

- [ ] **Step 1: Update CreatePointOfInterestAsync to set and persist CorrelationId**

Open `src/api/mytravels.api/Services/PointOfInterestService.cs`. Locate `CreatePointOfInterestAsync` method (around line 111-119). Update it to:

```csharp
public async Task<PointOfInterest> CreatePointOfInterestAsync(CreatePointOfInterestRequest request)
{
    var correlationId = Guid.NewGuid();
    
    var point = new PointOfInterest
    {
        // ... existing properties
        CorrelationId = correlationId
    };
    
    await _repository.AddAsync(point);
    await _unitOfWork.SaveChangesAsync();
    
    // Publish messages with the same correlationId
    var publishMessage = new PointOfInterestMessage
    {
        CorrelationId = correlationId,
        // ... other message fields
    };
    
    await _messagePublisher.PublishAsync("resize-image", publishMessage);
    await _messagePublisher.PublishAsync("append-formatted-address", publishMessage);
    await _messagePublisher.PublishAsync("append-image-tags", publishMessage);
    
    return point;
}
```

The key is: `point.CorrelationId = correlationId` is set **before** insert, not just on the message.

- [ ] **Step 2: Update UpdatePointOfInterestAsync to reuse or mint CorrelationId**

Locate `UpdatePointOfInterestAsync` method (around line 84). Find where it publishes to `resize-image`. Update to:

```csharp
public async Task<PointOfInterest> UpdatePointOfInterestAsync(int id, UpdatePointOfInterestRequest request)
{
    var point = await _repository.GetByIdAsync(id);
    
    // ... apply update properties
    
    // Reuse existing CorrelationId or mint a new one
    if (point.CorrelationId == null || point.CorrelationId == Guid.Empty)
    {
        point.CorrelationId = Guid.NewGuid();
    }
    
    await _unitOfWork.SaveChangesAsync();
    
    // Publish resize-image with the (possibly newly minted) CorrelationId
    var publishMessage = new PointOfInterestMessage
    {
        CorrelationId = point.CorrelationId ?? Guid.NewGuid(),
        // ... other message fields
    };
    
    await _messagePublisher.PublishAsync("resize-image", publishMessage);
    
    return point;
}
```

- [ ] **Step 3: Commit Task 9**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/api/mytravels.api/Services/PointOfInterestService.cs
git commit -m "feat: persist CorrelationId on POI create and update"
```

---

## Task 10: Update Messaging Program.cs to Register MyTravels.RabbitMQ ActivitySource

**Files:**
- Modify: `src/messaging/mytravels.messaging/Program.cs:28-31`

**Interfaces:**
- Consumes: Nothing new.
- Produces: `MyTravels.RabbitMQ` traces appear in messaging service's OTLP export.

- [ ] **Step 1: Add ActivitySource to Messaging tracing**

Open `src/messaging/mytravels.messaging/Program.cs`. Find where `.AddSource("Npgsql")` is called (around line 28-31). Add the new source right after:

```csharp
.AddSource("MyTravels.RabbitMQ")
```

The section should look like:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddSource("MyTravels.RabbitMQ")
        // ... rest of config
    );
```

- [ ] **Step 2: Commit Task 10**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Program.cs
git commit -m "feat: register MyTravels.RabbitMQ ActivitySource in messaging tracing"
```

---

## Task 11: Update Messaging Subscribers to Pass Failed Exchange Names and Add Structured Logging

**Files:**
- Modify: `src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs:1-72`
- Modify: `src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs:1-end`
- Modify: `src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs:1-end`

**Interfaces:**
- Consumes: `MessageSubscriberBase<T>` constructor now accepts `failedExchangeName` parameter.
- Produces: Structured error logs with `{PointOfInterestId}` and `{CorrelationId}`.

- [ ] **Step 1: Update ResizeImage subscriber**

Open `src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs`. 

**Add catch block** (lines 30-72 currently have only `try`/`finally`):

```csharp
try
{
    // ... existing resize work
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing ResizeImage message for POI {PointOfInterestId}, correlation {CorrelationId}",
        message.PointOfInterestId,
        message.CorrelationId);
    throw; // Let base class handle retry/dead-letter
}
finally
{
    // ... existing cleanup
}
```

**Update constructor** to pass failed exchange name to base:

```csharp
public ResizeImage(IModel channel, ILogger<ResizeImage> logger, ...) 
    : base(channel, logger, ExchangeNames.ResizeImageFailed, ...)
{
    // ...
}
```

- [ ] **Step 2: Update AppendFormattedAddress subscriber**

Open `src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs`. 

Locate the catch block around line 54. Update it to:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendFormattedAddress message for POI {PointOfInterestId}, correlation {CorrelationId}",
        message.PointOfInterestId,
        message.CorrelationId);
    throw;
}
```

**Update constructor** to pass failed exchange name:

```csharp
public AppendFormattedAddress(IModel channel, ILogger<AppendFormattedAddress> logger, ...)
    : base(channel, logger, ExchangeNames.AppendFormattedAddressFailed, ...)
{
    // ...
}
```

- [ ] **Step 3: Update AppendImageTags subscriber**

Open `src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs`. 

Locate the catch block around line 71. Update it to:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendImageTags message for POI {PointOfInterestId}, correlation {CorrelationId}",
        message.PointOfInterestId,
        message.CorrelationId);
    throw;
}
```

**Update constructor** to pass failed exchange name:

```csharp
public AppendImageTags(IModel channel, ILogger<AppendImageTags> logger, ...)
    : base(channel, logger, ExchangeNames.AppendImageTagsFailed, ...)
{
    // ...
}
```

- [ ] **Step 4: Commit Task 11**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs \
         src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs \
         src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs
git commit -m "feat: add structured error logging and failed exchange names to messaging subscribers"
```

---

## Task 12: Update AppendFormattedAddressSweeper to Per-Point Try/Catch

**Files:**
- Modify: `src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs:36-49`

**Interfaces:**
- Consumes: `point.CorrelationId` (now persisted from Task 2).
- Produces: One point's failure does not abort the batch; each point's error is logged with `{PointOfInterestId}` and `{CorrelationId}`.

- [ ] **Step 1: Wrap foreach body in per-point try/catch**

Open `src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs`. Locate `DoWorkAsync` method (around line 36-49). Currently it looks like:

```csharp
foreach (var point in pointsNeedingAddress)
{
    var address = await _mapsService.GetAddressAsync(point.Latitude, point.Longitude);
    point.FormattedAddress = address;
    // ... update point
}
```

Change to:

```csharp
foreach (var point in pointsNeedingAddress)
{
    try
    {
        var address = await _mapsService.GetAddressAsync(point.Latitude, point.Longitude);
        point.FormattedAddress = address;
        // ... update point
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Sweeper failed to geocode POI {PointOfInterestId}, correlation {CorrelationId}",
            point.Id,
            point.CorrelationId);
        continue; // Skip this point, process the rest
    }
}
```

- [ ] **Step 2: Remove or update outer catch/rethrow**

The current outer `try`/`catch` at line 45-49 that catches and rethrows can remain (it catches batch-level errors from DB operations outside the loop), but the per-point errors are now handled in the inner catch so they won't bubble up.

- [ ] **Step 3: Commit Task 12**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs
git commit -m "feat: add per-point error handling to geocoding sweeper"
```

---

## Task 13: Update Messaging Program.cs to Declare Failed Exchanges and Register Subscribers

**Files:**
- Modify: `src/messaging/mytravels.messaging/Program.cs:1-end` (Dependency Injection / subscriber registration section)

**Interfaces:**
- Consumes: 
  - `ExchangeNames.{ResizeImageFailed, AppendFormattedAddressFailed, AppendImageTagsFailed}` constants (Task 1).
  - Subscriber constructors now require failed exchange names (Task 11).
- Produces: Failed exchanges declared (fanout) during DI setup.

- [ ] **Step 1: Add failed exchange declarations in Program.cs DI setup**

Open `src/messaging/mytravels.messaging/Program.cs`. In the section where services are registered (e.g., `builder.Services.AddScoped<IMessageSubscriber, ResizeImage>`), add code to declare the failed exchanges:

```csharp
builder.Services.AddScoped(sp =>
{
    var channel = sp.GetRequiredService<IModel>();
    // Declare failed exchanges (fanout, non-durable)
    var declareTask = Task.Run(async () =>
    {
        await channel.ExchangeDeclareAsync(ExchangeNames.ResizeImageFailed, "fanout", durable: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(ExchangeNames.AppendFormattedAddressFailed, "fanout", durable: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(ExchangeNames.AppendImageTagsFailed, "fanout", durable: false, autoDelete: true);
    });
    declareTask.Wait();
    return channel;
});
```

Or if the channel is already a singleton/scoped, add the declarations once during host startup (e.g., in a hosted service or in `Main()` before the host runs).

- [ ] **Step 2: Commit Task 13**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Program.cs
git commit -m "feat: declare failed exchanges in messaging service DI"
```

---

## Task 14: Update Documentation (CLAUDE.md, SPEC.md)

**Files:**
- Modify: `CLAUDE.md` (remove stale MCP upload tools claim)
- Modify: `SPEC.md` (remove stale MCP upload tools claim, if present)

**Interfaces:**
- Produces: Accurate documentation stating MCP exposes only `search_pointofinterest` and `search_place`.

- [ ] **Step 1: Update CLAUDE.md**

Open `CLAUDE.md`. Search for references to `upload_photo` or `upload_photo_with_coordinates` MCP tools. Remove or correct any claims that these tools exist. Add a note that MCP currently exposes only `search_pointofinterest` and `search_place`.

- [ ] **Step 2: Update SPEC.md**

Open `SPEC.md`. If it contains similar stale claims about MCP upload tools, remove or correct them.

- [ ] **Step 3: Commit Task 14**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add CLAUDE.md SPEC.md
git commit -m "docs: correct stale MCP upload tool references"
```

---

## Task 15: Update Architecture Diagram (drawio/architecture.drawio)

**Files:**
- Modify: `drawio/architecture.drawio` (Monitoring and Application Architecture pages)

**Interfaces:**
- Produces: Diagram shows `MyTravels.RabbitMQ` Producer/Consumer spans; shows three `-failed` exchanges as fanout endpoints.

- [ ] **Step 1: Update Monitoring page**

Open `drawio/architecture.drawio` in draw.io. Navigate to the "Monitoring" page. Add `MyTravels.RabbitMQ` producer/consumer spans to the OTLP/telemetry arrows already drawn for `api` and `messaging` services. This is a visual note showing that RabbitMQ interactions are now traced.

- [ ] **Step 2: Update Application Architecture page**

Navigate to the "Application Architecture" page. Off each subscriber box (`resize-image`, `append-formatted-address`, `append-image-tags`), add small fanout-exchange icons labeled `*-failed`, showing the failure path alongside the happy path.

- [ ] **Step 3: Save drawio file**

Save the file. (If draw.io prompts for format, keep it as `.drawio` XML.)

- [ ] **Step 4: Commit Task 15**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add drawio/architecture.drawio
git commit -m "docs: update architecture diagrams for traceability spans and failed exchanges"
```

---

## Task 16: Smoke Test the End-to-End Traceability Flow

**Files:**
- No files modified; testing only.

**Interfaces:**
- Produces: Confidence that traces flow end-to-end and failed messages are published.

- [ ] **Step 1: Build and start stage 1 (Dockerize with observability)**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s/1-dockerize
docker compose up --build
```

Wait for all services to be healthy. In particular, verify that `api`, `messaging`, and `collector` (OTel collector) are running.

- [ ] **Step 2: Trigger a photo upload via the web UI**

Navigate to `http://localhost:3000` (or the configured web URL). Create a new point-of-interest with a photo. Monitor the logs in the Docker compose output. Look for:

- A trace ID assigned in the `api` service on POST `/pointofinterest`.
- That same trace ID (or a linked activity) appearing in `messaging` logs for `resize-image`, `append-formatted-address`, and `append-image-tags` consumers.
- Structured fields `{PointOfInterestId}` and `{CorrelationId}` in error logs.

- [ ] **Step 3: Trigger a deliberate failure (optional)**

To test the failure path, you can:

- Upload a corrupted image file (should fail in `resize-image`).
- Provide bad coordinates that cause geocoding to fail (should retry 3 times, then publish to `append-formatted-address-failed`).

Monitor logs and trace export (if Grafana/Tempo is running) to verify:

- Retry counts increment in logs.
- After 3 retries, the message appears in the failed exchange logs.
- No infinite requeue.

- [ ] **Step 4: Commit (smoke test passed)**

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add -A
git commit -m "test: smoke test traceability flow end-to-end"
```

(If smoke test reveals issues, fix them in preceding tasks and re-test before committing.)

---

## Plan Complete

All 16 tasks implement the traceability gaps spec:

1. ✓ Contract types (FailedMessage, failed exchanges, CorrelationId)
2. ✓ EF Core migration (CorrelationId column)
3. ✓ MessagePublisher trace propagation + publisher confirms
4. ✓ MessageSubscriberBase bounded retry + failed-message publishing
5. ✓ CronJobBase first-run guarding
6. ✓ Map services retry logging
7. ✓ API Program.cs ActivitySource
8. ✓ API error IDs linked to trace IDs
9. ✓ API CorrelationId persistence
10. ✓ Messaging Program.cs ActivitySource
11. ✓ Messaging subscribers structured logging + failed exchange names
12. ✓ Sweeper per-point error handling
13. ✓ Messaging failed-exchange declarations
14. ✓ Documentation corrections
15. ✓ Architecture diagrams updated
16. ✓ Smoke test

---

## Execution Options

**Plan complete and saved to `docs/superpowers/plans/2026-09-11-traceability-gaps.md`.**

Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration with high parallelism.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach would you prefer?**

