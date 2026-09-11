# Task 10: Update Messaging Program.cs to Register MyTravels.RabbitMQ ActivitySource

**Goal:** Register the MyTravels.RabbitMQ ActivitySource in the Messaging service's OpenTelemetry tracing configuration so Consumer spans are exported.

**Files:**
- Modify: `src/messaging/mytravels.messaging/Program.cs` (around lines 28-31, where `.AddSource("Npgsql")` is called)

**Interfaces (what this task produces for downstream tasks):**
- ActivitySource("MyTravels.RabbitMQ") is registered with `.AddSource()` in the Messaging service's OTel tracer provider.
- Consumer spans from MessageSubscriberBase are now exported to the collector.

**Consumes from earlier tasks:**
- Task 4: MessageSubscriberBase uses ActivitySource("MyTravels.RabbitMQ").

**Global Constraints:**
- ActivitySource name: `"MyTravels.RabbitMQ"` (exact, must match Tasks 3/4).
- Placed next to existing `.AddSource("Npgsql")` call.
- No changes to other OTel configuration.
- No new NuGet dependencies.

**Steps:**

### Step 1: Locate Messaging Program.cs OpenTelemetry configuration

Open `src/messaging/mytravels.messaging/Program.cs`.

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

This is typically around lines 28-31 but may vary.

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
dotnet build src/messaging/mytravels.messaging
```

Expect: Build succeeds.

### Step 4: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/messaging/mytravels.messaging/Program.cs
git commit -m "feat: register MyTravels.RabbitMQ ActivitySource in messaging tracing"
```

**Definition of Done:**
- `.AddSource("MyTravels.RabbitMQ")` added next to `.AddSource("Npgsql")` in Program.cs.
- Build succeeds with no errors.
- Changes committed with message provided above.
