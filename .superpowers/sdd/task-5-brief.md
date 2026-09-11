# Task 5: Update CronJobBase to Guard First Run

**Goal:** Ensure the first call to `DoWorkAsync()` in CronJobBase is protected by exception handling, preventing a first-run crash from terminating the host process.

**Files:**
- Modify: `src/common/mytravels.common/Services/CronJobBase.cs`

**Interfaces (what this task produces for downstream tasks):**
- CronJobBase.ExecuteAsync wraps both the first `DoWorkAsync()` call and the periodic loop in exception handling.
- Exception on first run is logged (not rethrown) and the periodic loop continues.

**Consumes from earlier tasks:**
- No external dependencies; purely internal refactoring.

**Global Constraints:**
- No new NuGet dependencies.
- Exception handling pattern: consistent with existing periodic loop handling.
- Log level: Error (match existing logging).
- First-run exception must not crash the host; loop must continue.

**Steps:**

### Step 1: Extract catch handler into private method

Open `src/common/mytravels.common/Services/CronJobBase.cs`.

Locate the existing `try`/`catch` block in the periodic loop (the one that wraps `DoWorkAsync()` in the `while` loop).

Extract the catch body into a new private method at the bottom of the class:

```csharp
private void HandleCronException(Exception ex)
{
    _logger.LogError(ex, "Cron job {JobName} failed", GetType().Name);
}
```

### Step 2: Update ExecuteAsync to wrap first run

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
            _logger.LogError(ex, "Cron job {JobName} failed", GetType().Name);
        }
        
        await Task.Delay(...);
    }
}
```

Modify it to:

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
        
        await Task.Delay(...);
    }
}
```

**Key change:** Both the first call and the loop now call `HandleCronException(ex)`.

### Step 3: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/common/mytravels.common
```

Expect: Build succeeds.

### Step 4: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/common/mytravels.common/Services/CronJobBase.cs
git commit -m "fix: guard first cron run with exception handling"
```

**Definition of Done:**
- First `DoWorkAsync()` call wrapped in try/catch.
- HandleCronException private method extracted.
- Both first run and periodic loop use the same exception handler.
- Build succeeds with no errors.
- Changes committed with message provided above.
- No new behavior introduced; only existing exception handling applied earlier.
