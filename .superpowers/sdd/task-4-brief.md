# Task 4: Update MessageSubscriberBase to Extract Traces, Implement Bounded Retry, Publish Failed Messages

**Goal:** Modify MessageSubscriberBase<T> to:
1. Extract traceparent from message headers and create linked Consumer Activity.
2. Implement bounded retry (max 3 attempts) with x-retry-count header.
3. Publish failed messages to `-failed` exchange after threshold.
4. Add structured error logging with PointOfInterestId and CorrelationId.
5. Ensure `-failed` exchanges are declared on initialization.

**Files:**
- Modify: `src/common/mytravels.common/Services/MessageSubscriberBase.cs`

**Interfaces (what this task produces for downstream tasks):**
- MessageSubscriberBase constructor now accepts `failedExchangeName` parameter (string).
- ReceivedAsync method extracts traceparent header and creates Consumer Activity linked to Producer.
- x-retry-count header incremented on each nack (starting at 0).
- After 3 failed attempts, message published to `-failed` exchange with FailedMessage structure.
- Structured error logs include {PointOfInterestId} and {CorrelationId} fields.

**Consumes from earlier tasks:**
- Task 1: `ExchangeNames` constants (for `-failed` exchange names).
- Task 1: `FailedMessage` type (used to structure failed message payload).
- Task 3: Messages have traceparent header and CorrelationId.

**Global Constraints:**
- Retry threshold: 3 attempts (match existing GoogleMapsService convention).
- Failed-message exchange naming: `{exchangeName}-failed` (fanout convention).
- ActivitySource name: `"MyTravels.RabbitMQ"` (same as Task 3).
- Activity kind: `ActivityKind.Consumer`.
- Activity name format: `"{exchange} consume"`.
- x-retry-count header: integer, defaults to 0 if missing.
- Failed messages published with PointOfInterestId, CorrelationId, OriginalExchange, ErrorMessage, FailedAt.
- Structured logging follows existing pattern: `.LogError(ex, "message {Field1} {Field2}", val1, val2)`.

**Steps:**

### Step 1: Add static ActivitySource and failedExchangeName field

Open `src/common/mytravels.common/Services/MessageSubscriberBase.cs`.

At the top of the class, add:
```csharp
private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");
private readonly string _failedExchangeName;
```

Ensure `using System.Diagnostics;` is at the top of the file.

### Step 2: Update constructor to accept failedExchangeName parameter

Locate the constructor. It currently looks something like:
```csharp
public MessageSubscriberBase(IModel channel, ILogger logger, ...)
{
    _channel = channel;
    _logger = logger;
    // ...
}
```

Update it to:
```csharp
public MessageSubscriberBase(IModel channel, ILogger logger, string failedExchangeName, ...)
{
    _channel = channel;
    _logger = logger;
    _failedExchangeName = failedExchangeName;
    // ... rest of init
}
```

Each derived subscriber class (in messaging) will pass `ExchangeNames.{ResizeImageFailed|AppendFormattedAddressFailed|AppendImageTagsFailed}` as the third parameter.

### Step 3: Replace ReceivedAsync method

Locate the `ReceivedAsync` method. Replace the entire method body with:

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
        ActivityContext.TryParse(traceparent, null, out linkedContext);
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

**Adaptation notes:**
- If `ProcessMessageAsync` is abstract or has a different signature, adjust accordingly.
- If the original `ReceivedAsync` had different logic (e.g., parsing, pre-processing), integrate that appropriately.
- The retry/failed-message logic replaces the old unconditional `BasicNackAsync(..., requeue: true)`.
- Failed messages are published as JSON anonymous objects (matching FailedMessage structure from Task 1).

### Step 4: Add initialization to declare failed exchanges

In the constructor (or in an initialization method), ensure the `-failed` exchange is declared. After setting _failedExchangeName, add:

```csharp
Task.Run(async () =>
{
    await _channel.ExchangeDeclareAsync(
        exchange: _failedExchangeName,
        type: "fanout",
        durable: false,
        autoDelete: true);
});
```

Or, declare all three exchanges at once in `Program.cs` (Task 13) if that's cleaner for the project.

### Step 5: Verify using statements

Ensure these are present:
```csharp
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json; // for JsonConvert
```

### Step 6: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/common/mytravels.common
```

Expect: Build succeeds.

### Step 7: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/MessageSubscriberBase.cs
git commit -m "feat: add trace extraction, bounded retry, and failed-message publishing to MessageSubscriberBase"
```

**Definition of Done:**
- ActivitySource("MyTravels.RabbitMQ") added as static field.
- failedExchangeName parameter added to constructor.
- ReceivedAsync extracts traceparent and creates Consumer Activity linked to Producer.
- x-retry-count header read and incremented (default 0).
- After 3 retries, message published to -failed exchange with FailedMessage structure.
- Structured error logs include {PointOfInterestId} and {CorrelationId}.
- Failed exchange declared (fanout) in initialization.
- Build succeeds with no errors.
- Changes committed with message provided above.
