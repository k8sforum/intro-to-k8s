# Task 9: Update API PointOfInterestService to Persist CorrelationId

**Goal:** Ensure `CorrelationId` is persisted in the `PointOfInterest` entity during create and update operations, so it survives beyond the initial message publish.

**Files:**
- Modify: `src/api/mytravels.api/Services/PointOfInterestService.cs` (two methods: CreatePointOfInterestAsync and UpdatePointOfInterestAsync)

**Interfaces (what this task produces for downstream tasks):**
- `CreatePointOfInterestAsync` mints a `CorrelationId` Guid, sets it on the entity before insert, and reuses it on all three message publishes.
- `UpdatePointOfInterestAsync` reuses the existing `point.CorrelationId` (if populated) on the resize-image publish; if null, mints a new one and persists it.

**Consumes from earlier tasks:**
- Task 1: `PointOfInterest.CorrelationId` property exists.
- Task 2: Migration adds the database column.
- Task 3: MessagePublisher reads `CorrelationId` via reflection.

**Global Constraints:**
- No new NuGet dependencies.
- CorrelationId is minted once per entity (not per message).
- Same CorrelationId used for all three fanout exchanges in CreatePointOfInterestAsync.
- UpdatePointOfInterestAsync reuses existing CorrelationId (not re-minting).

**Steps:**

### Step 1: Update CreatePointOfInterestAsync

Open `src/api/mytravels.api/Services/PointOfInterestService.cs`.

Locate the `CreatePointOfInterestAsync` method (around line 111-119).

Update it to:

```csharp
public async Task<PointOfInterest> CreatePointOfInterestAsync(CreatePointOfInterestRequest request)
{
    var correlationId = Guid.NewGuid();
    
    var point = new PointOfInterest
    {
        // ... existing properties (Name, Description, etc.)
        CorrelationId = correlationId
    };
    
    await _repository.AddAsync(point);
    await _unitOfWork.SaveChangesAsync();
    
    // Publish messages with the same correlationId
    var publishMessage = new PointOfInterestMessage
    {
        CorrelationId = correlationId,
        PointOfInterestId = point.Id,
        // ... other message fields
    };
    
    await _messagePublisher.PublishAsync("resize-image", publishMessage);
    await _messagePublisher.PublishAsync("append-formatted-address", publishMessage);
    await _messagePublisher.PublishAsync("append-image-tags", publishMessage);
    
    return point;
}
```

**Key changes:**
1. Mint `correlationId = Guid.NewGuid()` at the start.
2. Set `point.CorrelationId = correlationId` **before** calling `SaveChangesAsync()`.
3. Reuse the same `correlationId` for all three message publishes.

### Step 2: Update UpdatePointOfInterestAsync

Locate the `UpdatePointOfInterestAsync` method (around line 84).

Find where it publishes to the "resize-image" exchange. Update it to:

```csharp
public async Task<PointOfInterest> UpdatePointOfInterestAsync(int id, UpdatePointOfInterestRequest request)
{
    var point = await _repository.GetByIdAsync(id);
    
    // ... apply update properties (e.g., point.Description = request.Description)
    
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
        PointOfInterestId = point.Id,
        // ... other message fields
    };
    
    await _messagePublisher.PublishAsync("resize-image", publishMessage);
    
    return point;
}
```

**Key changes:**
1. Check if `point.CorrelationId` is null or empty; if so, mint a new Guid.
2. Set `point.CorrelationId` on the entity.
3. Call `SaveChangesAsync()` to persist it.
4. Reuse that CorrelationId on the message publish.

### Step 3: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/api/mytravels.api
```

Expect: Build succeeds.

### Step 4: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/api/mytravels.api/Services/PointOfInterestService.cs
git commit -m "feat: persist CorrelationId on POI create and update"
```

**Definition of Done:**
- CreatePointOfInterestAsync mints correlationId and sets it on entity before insert.
- CreatePointOfInterestAsync reuses same correlationId for all three publishes.
- UpdatePointOfInterestAsync reuses existing point.CorrelationId or mints a new one.
- UpdatePointOfInterestAsync persists the CorrelationId to the database.
- Build succeeds with no errors.
- Changes committed with message provided above.
