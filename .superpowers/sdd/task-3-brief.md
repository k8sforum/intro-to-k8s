# Task 3: Update MessagePublisher to Propagate Traces, Publisher Confirms, and CorrelationId Headers

**Goal:** Modify MessagePublisher to:
1. Add static ActivitySource for tracing (Producer spans).
2. Propagate W3C `traceparent` header from Activity.Current.
3. Set AMQP CorrelationId from message's CorrelationId field.
4. Enable publisher confirms to detect silent message loss.
5. Log warnings on nack/return.

**Files:**
- Modify: `src/common/mytravels.common/Services/MessagePublisher.cs`

**Interfaces (what this task produces for downstream tasks):**
- MessagePublisher now creates Producer-kind Activities using ActivitySource("MyTravels.RabbitMQ").
- Message headers include `"traceparent" → Activity.Current?.Id`.
- AMQP CorrelationId property set from message's CorrelationId (read via reflection).
- Publisher confirms enabled; nack/return logged at Warning level.

**Consumes from earlier tasks:**
- Task 1: `IMessage` interface assumed on generic `T`, and message has `CorrelationId` field (read via reflection).
- Task 5/6 (Program.cs): Will register ActivitySource("MyTravels.RabbitMQ") so it's exportable.

**Global Constraints:**
- No new NuGet dependencies; use existing System.Diagnostics.Activity.
- ActivitySource name: `"MyTravels.RabbitMQ"` (exact).
- Activity kind: `ActivityKind.Producer`.
- Activity name format: `"{exchange} publish"`.
- Publisher confirms: enable `ConfirmSelectAsync()` and await the result.
- Mandatory: true (detect unroutable messages).
- Warning log on nack/return includes `exchange` and `CorrelationId`.
- Reflection to read message.CorrelationId (not a direct property of the generic constraint).

**Steps:**

### Step 1: Add static ActivitySource field

Open `src/common/mytravels.common/Services/MessagePublisher.cs`.

At the top of the `MessagePublisher` class, add:
```csharp
private static readonly ActivitySource _activitySource = new("MyTravels.RabbitMQ");
```

Ensure `using System.Diagnostics;` is at the top of the file.

### Step 2: Replace PublishAsync method body

Locate the `PublishAsync` method in MessagePublisher. Replace the entire method body with:

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
        var correlationIdProperty = message.GetType().GetProperty("CorrelationId");
        if (correlationIdProperty != null)
        {
            var messageCorrelationId = correlationIdProperty.GetValue(message) as Guid?;
            if (messageCorrelationId.HasValue && messageCorrelationId != Guid.Empty)
            {
                properties.CorrelationId = messageCorrelationId.ToString();
            }
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
                correlationIdProperty?.GetValue(message)
            );
        }
    }
}
```

**Adaptation notes:**
- If the original `PublishAsync` used different variable names, parameter names, or structure, adapt the code to fit the existing patterns.
- The key additions are: Activity wrapping, traceparent header, CorrelationId reflection-read, ConfirmSelectAsync, and nack/return warning.
- If `JsonConvert` or `Encoding` are not already using statements, ensure they're present (should be).

### Step 3: Verify using statements

Ensure these using statements are at the top:
```csharp
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json; // for JsonConvert
```

(Adjust based on what's already present in the file.)

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
git add src/common/mytravels.common/Services/MessagePublisher.cs
git commit -m "feat: add trace propagation, publisher confirms, and correlation headers to MessagePublisher"
```

**Definition of Done:**
- ActivitySource("MyTravels.RabbitMQ") added as static field.
- PublishAsync creates Producer Activity named "{exchange} publish".
- traceparent header set from Activity.Current?.Id.
- CorrelationId read via reflection and set on BasicProperties.
- ConfirmSelectAsync enabled; confirm result awaited.
- Mandatory: true.
- Warning log on nack/return includes exchange and CorrelationId.
- Build succeeds with no errors.
- Changes committed with message provided above.
