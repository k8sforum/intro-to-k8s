# Task 7: Update API Program.cs to Register MyTravels.RabbitMQ ActivitySource

**Goal:** Register the MyTravels.RabbitMQ ActivitySource in the API's OpenTelemetry tracing configuration so Producer/Consumer spans from MessagePublisher and MessageSubscriberBase are exported.

**Files:**
- Modify: `src/api/mytravels.api/Program.cs` (around lines 29-32, where `.AddSource("Npgsql")` is called)

**Interfaces (what this task produces for downstream tasks):**
- ActivitySource("MyTravels.RabbitMQ") is registered with `.AddSource()` in the API's OTel tracer provider.
- Producer and Consumer spans from RabbitMQ message operations are now exported to the collector.

**Consumes from earlier tasks:**
- Task 3: MessagePublisher uses ActivitySource("MyTravels.RabbitMQ").
- Task 4: MessageSubscriberBase uses ActivitySource("MyTravels.RabbitMQ").

**Global Constraints:**
- ActivitySource name: `"MyTravels.RabbitMQ"` (exact, must match Tasks 3/4).
- Placed next to existing `.AddSource("Npgsql")` call.
- No changes to other OTel configuration.
- No new NuGet dependencies.

**Steps:**

### Step 1: Locate API Program.cs OpenTelemetry configuration

Open `src/api/mytravels.api/Program.cs`.

Find the OpenTelemetry builder section that looks like:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        // ... possibly more configuration
    );
```

This is typically around lines 29-32 but may vary.

### Step 2: Add ActivitySource registration

After the `.AddSource("Npgsql")` line, add a new line:

```csharp
.AddSource("MyTravels.RabbitMQ")
```

So the section becomes:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Npgsql")
        .AddSource("MyTravels.RabbitMQ")
        // ... rest of configuration
    );
```

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
git add src/api/mytravels.api/Program.cs
git commit -m "feat: register MyTravels.RabbitMQ ActivitySource in API tracing"
```

**Definition of Done:**
- `.AddSource("MyTravels.RabbitMQ")` added next to `.AddSource("Npgsql")` in Program.cs.
- Build succeeds with no errors.
- Changes committed with message provided above.
