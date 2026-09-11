# Task 13: Update Messaging Program.cs to Declare Failed Exchanges and Register Subscribers

**Goal:** Ensure the three failed exchanges are declared (fanout, non-durable) during DI setup in the Messaging service, and verify subscriber registrations include the failed exchange names.

**Files:**
- Modify: `src/messaging/mytravels.messaging/Program.cs` (DI/subscriber registration section)

**Interfaces (what this task produces for downstream tasks):**
- Three `-failed` exchanges are declared (fanout, non-durable) so MessageSubscriberBase can publish to them.
- Subscribers are registered with their failed exchange names in constructors (Task 11 will pass these).

**Consumes from earlier tasks:**
- Task 1: `ExchangeNames.{ResizeImageFailed|AppendFormattedAddressFailed|AppendImageTagsFailed}` constants.
- Task 4: MessageSubscriberBase constructor accepts failedExchangeName.
- Task 11: Subscribers pass these names to base constructor.

**Global Constraints:**
- Failed exchanges: fanout, non-durable, auto-delete=true.
- Exchange names: ResizeImageFailed="resize-image-failed", AppendFormattedAddressFailed="append-formatted-address-failed", AppendImageTagsFailed="append-image-tags-failed".
- Exchanges declared at startup (in DI setup or hosted service).
- No new NuGet dependencies.

**Steps:**

### Step 1: Locate subscriber registration in Program.cs

Open `src/messaging/mytravels.messaging/Program.cs`.

Find the section where services/subscribers are registered. It typically looks like:

```csharp
builder.Services.AddScoped<IMessageSubscriber, ResizeImage>();
builder.Services.AddScoped<IMessageSubscriber, AppendFormattedAddress>();
builder.Services.AddScoped<IMessageSubscriber, AppendImageTags>();
// ... other registrations
```

### Step 2: Add failed exchange declarations

Before or after the subscriber registrations, add a section to declare the failed exchanges:

```csharp
// Declare failed exchanges (fanout, non-durable)
builder.Services.AddSingleton(sp =>
{
    var channel = sp.GetRequiredService<IModel>();
    _ = Task.Run(async () =>
    {
        await channel.ExchangeDeclareAsync(ExchangeNames.ResizeImageFailed, "fanout", durable: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(ExchangeNames.AppendFormattedAddressFailed, "fanout", durable: false, autoDelete: true);
        await channel.ExchangeDeclareAsync(ExchangeNames.AppendImageTagsFailed, "fanout", durable: false, autoDelete: true);
    });
    return channel;
});
```

Alternatively, if you prefer a hosted service pattern or if the channel is already a singleton, wrap the declarations in a hosted service or an initialization method that runs once at startup.

### Step 3: Verify subscriber constructors receive failed exchange names

By Task 11, each subscriber constructor will have been updated to accept and pass the failed exchange name to MessageSubscriberBase. For example:

```csharp
public ResizeImage(IModel channel, ILogger<ResizeImage> logger, ...)
    : base(channel, logger, ExchangeNames.ResizeImageFailed, ...)
```

Ensure the DI registration in Program.cs provides all required constructor dependencies (IModel, ILogger<T>, any other params).

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
git add src/messaging/mytravels.messaging/Program.cs
git commit -m "feat: declare failed exchanges in messaging service DI"
```

**Definition of Done:**
- Three failed exchanges declared in Program.cs startup.
- Exchange names use ExchangeNames constants (ResizeImageFailed, AppendFormattedAddressFailed, AppendImageTagsFailed).
- Exchanges are fanout, durable=false, autoDelete=true.
- Subscriber DI registrations provide all required dependencies (including IModel, ILogger<T>).
- Build succeeds with no errors.
- Changes committed with message provided above.
