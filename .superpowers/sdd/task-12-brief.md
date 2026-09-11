# Task 12: Update AppendFormattedAddressSweeper to Per-Point Try/Catch

**Goal:** Modify the geocoding sweeper to handle per-point failures individually, so one point's failure doesn't abort the entire batch. Each point's error is logged with {PointOfInterestId} and {CorrelationId}.

**Files:**
- Modify: `src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs` (DoWorkAsync method, lines 36-49)

**Interfaces (what this task produces for downstream tasks):**
- Sweeper processes all points in the batch, even if individual points fail.
- Each point's error is logged with {PointOfInterestId} and {CorrelationId}.
- No load-bearing dependencies on this task for later tasks.

**Consumes from earlier tasks:**
- Task 2: `PointOfInterest.CorrelationId` column exists and is populated.

**Global Constraints:**
- No new NuGet dependencies.
- Structured logging includes {PointOfInterestId} and {CorrelationId}.
- Log level: Error.
- One point's failure does NOT abort the batch; loop continues to the next point.

**Steps:**

### Step 1: Locate AppendFormattedAddressSweeper

Open `src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs`.

Locate the `DoWorkAsync` method (around line 36-49). It currently looks something like:

```csharp
foreach (var point in pointsNeedingAddress)
{
    var address = await _mapsService.GetAddressAsync(point.Latitude, point.Longitude);
    point.FormattedAddress = address;
    // ... update point
}
```

### Step 2: Wrap foreach body in per-point try/catch

Replace the loop body with:

```csharp
foreach (var point in pointsNeedingAddress)
{
    try
    {
        var address = await _mapsService.GetAddressAsync(point.Latitude, point.Longitude);
        point.FormattedAddress = address;
        // ... update point (preserve existing logic)
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

**Key changes:**
1. Wrap the body of the foreach in try/catch.
2. Log error with {PointOfInterestId} and {CorrelationId}.
3. Call `continue` to skip to the next point (don't rethrow).

### Step 3: Review outer try/catch

After the foreach loop, there may be an outer try/catch (around line 45-49) that catches batch-level errors (e.g., from SaveChangesAsync). This catch can remain as-is; it handles errors outside the per-point loop (like database failures).

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
git add src/messaging/mytravels.messaging/Services/AppendFormattedAddressSweeper.cs
git commit -m "feat: add per-point error handling to geocoding sweeper"
```

**Definition of Done:**
- Foreach body wrapped in try/catch.
- Catch logs error with {PointOfInterestId} and {CorrelationId}.
- Catch calls `continue` (does not rethrow).
- One point's failure does not abort the batch.
- Build succeeds with no errors.
- Changes committed with message provided above.
