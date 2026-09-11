# Task 11: Update Messaging Subscribers to Pass Failed Exchange Names and Add Structured Logging

**Goal:** Update three subscriber classes (ResizeImage, AppendFormattedAddress, AppendImageTags) to:
1. Pass failed exchange names to MessageSubscriberBase constructor.
2. Add catch blocks with structured error logging (where missing).
3. Log with {PointOfInterestId} and {CorrelationId} fields.

**Files:**
- Modify: `src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs`
- Modify: `src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs`
- Modify: `src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs`

**Interfaces (what this task produces for downstream tasks):**
- Each subscriber passes its failed exchange name to MessageSubscriberBase constructor.
- Each subscriber has structured error logging with {PointOfInterestId} and {CorrelationId}.

**Consumes from earlier tasks:**
- Task 1: `ExchangeNames.{ResizeImageFailed|AppendFormattedAddressFailed|AppendImageTagsFailed}` constants.
- Task 4: MessageSubscriberBase constructor accepts failedExchangeName parameter.

**Global Constraints:**
- Failed exchange names: ResizeImage → "resize-image-failed", AppendFormattedAddress → "append-formatted-address-failed", AppendImageTags → "append-image-tags-failed".
- Structured log format: `.LogError(ex, "message {PointOfInterestId} {CorrelationId}", poi_id, correlation_id)`.
- Log level: Error (not higher, not lower).
- ResizeImage currently has only try/finally; add catch block.
- AppendFormattedAddress and AppendImageTags already have catch; update logging.

**Steps:**

### Step 1: Update ResizeImage subscriber

Open `src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs`.

**Update constructor** to pass failed exchange name:

```csharp
public ResizeImage(IModel channel, ILogger<ResizeImage> logger, ...)
    : base(channel, logger, ExchangeNames.ResizeImageFailed, ...)
{
    // ... rest of init
}
```

**Add catch block** to the ProcessMessageAsync method (currently only try/finally):

Locate lines around 30-72. The current code looks like:

```csharp
try
{
    // ... resize work
}
finally
{
    // ... cleanup
}
```

Change to:

```csharp
try
{
    // ... resize work
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
    // ... cleanup
}
```

### Step 2: Update AppendFormattedAddress subscriber

Open `src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs`.

**Update constructor** to pass failed exchange name:

```csharp
public AppendFormattedAddress(IModel channel, ILogger<AppendFormattedAddress> logger, ...)
    : base(channel, logger, ExchangeNames.AppendFormattedAddressFailed, ...)
{
    // ... rest of init
}
```

**Update existing catch block** around line 54:

Change:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendFormattedAddress message");
    throw;
}
```

To:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendFormattedAddress message for POI {PointOfInterestId}, correlation {CorrelationId}",
        message.PointOfInterestId,
        message.CorrelationId);
    throw;
}
```

### Step 3: Update AppendImageTags subscriber

Open `src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs`.

**Update constructor** to pass failed exchange name:

```csharp
public AppendImageTags(IModel channel, ILogger<AppendImageTags> logger, ...)
    : base(channel, logger, ExchangeNames.AppendImageTagsFailed, ...)
{
    // ... rest of init
}
```

**Update existing catch block** around line 71:

Change:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendImageTags message");
    throw;
}
```

To:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing AppendImageTags message for POI {PointOfInterestId}, correlation {CorrelationId}",
        message.PointOfInterestId,
        message.CorrelationId);
    throw;
}
```

### Step 4: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/messaging/mytravels.messaging
```

Expect: Build succeeds.

### Step 5: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Subscribers/ResizeImage.cs \
         src/messaging/mytravels.messaging/Subscribers/AppendFormattedAddress.cs \
         src/messaging/mytravels.messaging/Subscribers/AppendImageTags.cs
git commit -m "feat: add structured error logging and failed exchange names to messaging subscribers"
```

**Definition of Done:**
- ResizeImage constructor passes ExchangeNames.ResizeImageFailed to base.
- ResizeImage has catch block with structured logging.
- AppendFormattedAddress constructor passes ExchangeNames.AppendFormattedAddressFailed to base.
- AppendFormattedAddress catch block updated with structured logging.
- AppendImageTags constructor passes ExchangeNames.AppendImageTagsFailed to base.
- AppendImageTags catch block updated with structured logging.
- All three log {PointOfInterestId} and {CorrelationId} fields.
- Build succeeds with no errors.
- Changes committed with message provided above.
