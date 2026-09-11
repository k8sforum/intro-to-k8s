# Task 1: Add Contract Types (FailedMessage, Exchange Constants, CorrelationId Property)

**Goal:** Add three new failed-exchange constants, create a new `FailedMessage` contract type, and add a nullable `CorrelationId` property to `PointOfInterest`.

**Files:**
- Modify: `src/contract/mytravels.contract/Constants/ExchangeNames.cs`
- Create: `src/contract/mytravels.contract/Messages/FailedMessage.cs`
- Modify: `src/contract/mytravels.contract/Entities/PointOfInterest.cs`

**Interfaces (what this task produces for downstream tasks):**
- Three new public string constants in `ExchangeNames`:
  - `ResizeImageFailed = "resize-image-failed"`
  - `AppendFormattedAddressFailed = "append-formatted-address-failed"`
  - `AppendImageTagsFailed = "append-image-tags-failed"`
- New `FailedMessage` class implementing `IMessage` interface with properties:
  - `Guid CorrelationId { get; set; }`
  - `int PointOfInterestId { get; set; }`
  - `string OriginalExchange { get; set; }`
  - `string ErrorMessage { get; set; }`
  - `DateTime FailedAt { get; set; }`
- `PointOfInterest.CorrelationId` property added: `public Guid? CorrelationId { get; set; }`

**Global Constraints:**
- No new NuGet dependencies (use existing OTel, ASP.NET Core Activity API).
- FailedMessage must implement `IMessage` interface (check existing pattern in contract project).
- CorrelationId column is nullable `Guid?` (matches design decision 4 from spec).
- Failed-message exchange naming: `{exchangeName}-failed` (fanout convention, documented in design decision 3).

**Steps:**

### Step 1: Add three `-failed` exchange constants to ExchangeNames.cs

Open `src/contract/mytravels.contract/Constants/ExchangeNames.cs`. 

Add three new public constants (append to end of existing constants):
```csharp
public const string ResizeImageFailed = "resize-image-failed";
public const string AppendFormattedAddressFailed = "append-formatted-address-failed";
public const string AppendImageTagsFailed = "append-image-tags-failed";
```

### Step 2: Create FailedMessage.cs

Create new file `src/contract/mytravels.contract/Messages/FailedMessage.cs` with:
```csharp
using System;
using MyTravels.Common.Contracts;

namespace MyTravels.Contract.Messages
{
    public class FailedMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
        public int PointOfInterestId { get; set; }
        public string OriginalExchange { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime FailedAt { get; set; }
    }
}
```

Verify that `IMessage` interface exists in `MyTravels.Common.Contracts` (it should — existing message types in the project use it). If the namespace is different, adjust the using statement.

### Step 3: Add CorrelationId property to PointOfInterest.cs

Open `src/contract/mytravels.contract/Entities/PointOfInterest.cs`. 

Locate the existing properties (e.g., `Description`, `Tags`, `UpdatedAt`). Add this property (place it near other metadata properties, before navigation properties):
```csharp
public Guid? CorrelationId { get; set; }
```

### Step 4: Commit

```bash
cd /Users/tshepo.ntlhokoa/Development/k8sforum/intro-to-k8s
git add src/contract/mytravels.contract/Constants/ExchangeNames.cs \
         src/contract/mytravels.contract/Messages/FailedMessage.cs \
         src/contract/mytravels.contract/Entities/PointOfInterest.cs
git commit -m "feat: add traceability contract types (FailedMessage, failed exchanges, CorrelationId)"
```

**Definition of Done:**
- All three constants added to ExchangeNames.cs
- FailedMessage.cs created with correct IMessage implementation
- PointOfInterest.CorrelationId property added as nullable Guid
- Changes committed with message provided above
- No build errors; existing tests still pass (if any exist in contract project)
