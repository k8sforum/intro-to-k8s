# Task 8: Update API ApiExceptionMiddleware to Use TraceId as ErrorId

**Goal:** Link API error responses to trace IDs so support can jump directly to the trace in the observability backend, instead of relying on random error IDs.

**Files:**
- Modify: `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs` (two locations: HandleServerErrorAsync and HandleClientErrorAsync)

**Interfaces (what this task produces for downstream tasks):**
- Error responses include `ErrorId = Activity.Current?.TraceId.ToString()` (the trace ID from the active trace).
- Falls back to a random Guid.NewGuid().ToString("N") if no trace is active.

**Consumes from earlier tasks:**
- No external dependencies; uses existing System.Diagnostics.Activity.

**Global Constraints:**
- No new NuGet dependencies.
- Prefer TraceId when a trace is active: `Activity.Current?.TraceId.ToString()`.
- Fallback to random Guid: `Guid.NewGuid().ToString("N")`.
- Must be updated in BOTH `HandleServerErrorAsync` and `HandleClientErrorAsync` methods.

**Steps:**

### Step 1: Locate ApiExceptionMiddleware file

Open `src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs`.

Find the two methods:
1. `HandleServerErrorAsync` (around line 61)
2. `HandleClientErrorAsync` (around line 79)

### Step 2: Update HandleServerErrorAsync

Locate the line where `ErrorId` is assigned (around line 61). It currently looks like:

```csharp
Id = Guid.NewGuid().ToString("N")
```

Change it to:

```csharp
Id = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
```

### Step 3: Update HandleClientErrorAsync

Locate the similar line in `HandleClientErrorAsync` (around line 79). Apply the same change:

```csharp
Id = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
```

### Step 4: Add using directive if needed

Ensure `using System.Diagnostics;` is at the top of the file. If it's not present, add it.

### Step 5: Compile to verify no errors

Run:
```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
dotnet build src/api/mytravels.api
```

Expect: Build succeeds.

### Step 6: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/api/mytravels.api/Middleware/ApiExceptionMiddleware.cs
git commit -m "feat: link API error IDs to trace IDs for easier support lookup"
```

**Definition of Done:**
- `HandleServerErrorAsync` Id assignment updated to prefer TraceId.
- `HandleClientErrorAsync` Id assignment updated to prefer TraceId.
- Both fallback to random Guid when no trace is active.
- using System.Diagnostics; present in file.
- Build succeeds with no errors.
- Changes committed with message provided above.
