# Async Image Description & Tagging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move image description/tagging (currently a synchronous `POST {id}/describe` scaffold) to the same async, message-driven pattern already used by `resize-image` and `append-formatted-address`, so every newly-uploaded photo gets a `Description` and `Tags` automatically, with no user action and no synchronous Claude call in the request path.

**Architecture:** `PointOfInterestService.CreatePointOfInterestAsync` gains a third parallel publish to a new `append-image-tags` fanout exchange. A new `AppendImageTags` hosted-service subscriber in `mytravels.messaging` (same `MessageSubscriberBase<PointOfInterestMessage>` pattern as `ResizeImage`/`AppendFormattedAddress`) reads the original image, calls the existing `IImageDescriptionService`, writes `PointOfInterest.Description` directly via EF and writes tags through the existing `UpdatePointOfInterestTagsAsync` stored-proc path. The synchronous describe endpoint, its DTO plumbing on `IPointOfInterestService`, and the "Describe" button UI are deleted. `AnthropicApiKey` wiring moves from `api` to `messaging` in all environment/secret surfaces.

**Tech Stack:** .NET 10 (EF Core + Npgsql, RabbitMQ.Client), React/TypeScript (Vite), Postgres stored functions (`plpgsql`), Docker Compose, raw `kubectl` manifests, Argo CD.

**Spec:** [prompts/async image description design prompt.md](../../../prompts/async%20image%20description%20design%20prompt.md) — the plan below implements it task-by-task; executors should still skim the spec for narrative context.

## Global Constraints

- **No automated test suite exists anywhere in this repo** (confirmed: no test projects in `mytravels.sln`, no test script in `package.json`). "Testable deliverable" in every task below means: the affected project builds/lints cleanly, and — where practical — a manual, described verification (curl, docker compose, notebook cell) is run and its output checked. Do not invent a test framework to satisfy the plan template.
- **Correction to the spec:** §"Tag persistence" and the `AppendImageTags.cs` sketch in `prompts/async image description design prompt.md` say `UpdatePointOfInterestTagsAsync` takes `PointOfInterestTag { int PointOfInterestId; string TagName; }`. That is wrong. The real signature (`ICoreDbContext.cs:29`) is `Task<int> UpdatePointOfInterestTagsAsync(List<SavePointOfInterestDto> dtos, CancellationToken cancellationToken)`, where `SavePointOfInterestDto { int PointOfInterestId; List<string> Tags; }` (`mytravels.contract/Dtos/SavePointOfInterestDto.cs`). `PointOfInterestTag` is a private type used only inside `CoreDbContext.UpdatePointOfInterestTagsAsync`'s own implementation to build the JSON blob passed to `spUpdatePointOfInterestTags`. Task 3 below uses the correct type.
- **Gap the spec missed:** `GET api/PointOfInterest` (`GetAllPointsOfInterestAsync`) does not run a LINQ query — it calls the Postgres function `public.spGetPointOfInterest()`, whose `RETURNS TABLE` column list is hard-coded in `mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterests.sql`. Adding an EF migration that adds a `Description` column to the `PointOfInterests` table is **not suffici­ent** by itself — the SQL function must also be taught to return the new column, or the API will keep returning nulls forever regardless of what's in the database. Task 1 covers this.
- Every new/changed subscriber follows the existing `MessageSubscriberBase<T>` convention exactly: `base(logger, configuration, ExchangeNames.X, ExchangeNames.X)` (queue name == exchange name), a fresh DI scope per message, and the static `SemaphoreSlim(1, 1)` serialization guard used by both `ResizeImage.cs` and `AppendFormattedAddress.cs`.
- Image source for description/tagging is always `BucketNames.NewUploadedImagesContainer` (the original bucket) — never assume `ResizeImage` has already run, since the three subscribers now fire independently off the same publish.
- Out of scope (per the spec, do not touch): rotating the live-looking Anthropic key in `1-dockerize/.env`; the dead `ContentSafetyEndpoint`/`ContentSafetyKey`/`GoogleApiKey` entries on `messaging-secret`; retry/DLQ handling beyond what `MessageSubscriberBase` already does (nack + requeue); backfilling `Description`/`Tags` for pre-existing POIs.

---

### Task 1: Description column — entity, DTOs, stored functions, migration

**Files:**
- Modify: `src/common/mytravels.contract/Entities/PointOfInterest.cs`
- Modify: `src/common/mytravels.contract/Dtos/PointOfInterestDto.cs`
- Modify: `src/common/mytravels.contract/Responses/GetPointOfInterestResponse.cs`
- Modify: `src/api/mytravels.api/Extensions/ExtensionsMethods.cs`
- Modify: `src/common/mytravels.domain/CoreDbContext.cs` (the `SearchPointsOfInterestByFormattedAddressAsync` LINQ projection — the one query path that isn't backed by a `.sql` function)
- Modify: `src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterests.sql`
- Modify: `src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterestById.sql`
- Modify: `src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterestByTagName.sql`
- Create: `src/common/mytravels.domain/Migrations/<timestamp>_AddPointOfInterestDescription.cs` (+ `.Designer.cs`, generated)
- Modify: `src/common/mytravels.domain/Migrations/CoreDbContextModelSnapshot.cs` (generated by the same command)

**Interfaces:**
- Produces: `PointOfInterest.Description` (`string?`, unbounded `text`), `PointOfInterestDto.Description` (`string?`), `GetPointOfInterestResponse.Description` (`string`) — consumed by Task 2 (messaging subscriber writes it) and Task 4 (frontend reads it via `PointOfInterestDto`/`types.ts`).

- [ ] **Step 1: Add `Description` to the entity**

Edit `src/common/mytravels.contract/Entities/PointOfInterest.cs` — add after `Reason`:

```csharp
    [StringLength(500)]
    public string Reason { get; set; }
    public string? Description { get; set; }
}
```

No `[StringLength]` — Claude descriptions are free text of variable length, unlike `Tag.Name`'s 30-char cap (a different, short-label constraint).

- [ ] **Step 2: Add `Description` to `PointOfInterestDto`**

Edit `src/common/mytravels.contract/Dtos/PointOfInterestDto.cs`:

```csharp
    [StringLength(300)]
    public string FormattedAddress { get; set; }
    public string? Description { get; set; }
    public List<TagDto> Tags { get; set; } = new();
```

- [ ] **Step 3: Add `Description` to `GetPointOfInterestResponse`**

Edit `src/common/mytravels.contract/Responses/GetPointOfInterestResponse.cs` — add after `FormattedAddress`:

```csharp
    public string FormattedAddress { get; set; }
    public string Description { get; set; }
    public bool ImageResized { get; set; }
```

- [ ] **Step 4: Map `Description` in `ToDto`**

Edit `src/api/mytravels.api/Extensions/ExtensionsMethods.cs` — in the `PointOfInterestDto` object initializer, add:

```csharp
                    FormattedAddress = first.FormattedAddress,
                    Description = first.Description,
                    Latitude = first.Latitude,
```

- [ ] **Step 5: Map `Description` in the search LINQ projection**

Edit `src/common/mytravels.domain/CoreDbContext.cs`, in `SearchPointsOfInterestByFormattedAddressAsync` — add `Description = p.Description,` to the `Select` projection, next to `FormattedAddress = p.FormattedAddress,`.

- [ ] **Step 6: Add `"Description"` to the three stored functions**

All three functions share the same `RETURNS TABLE` shape as `GetPointOfInterestResponse`. In each of `public.spGetPointOfInterests.sql`, `public.spGetPointOfInterestById.sql`, `public.spGetPointOfInterestByTagName.sql`:

1. Add `"Description" TEXT,` to the `RETURNS TABLE (...)` list, right after `"FormattedAddress" VARCHAR(300),`.
2. Add `T."Description",` (or `poi."Description",` in `spGetPointOfInterestById.sql`, which has no subquery aliasing) to the outer `SELECT` list, right after the `"FormattedAddress"` column.
3. Add `poi."Description",` to the inner `SELECT` (in `spGetPointOfInterests.sql` and `spGetPointOfInterestByTagName.sql`, which both use a `FROM (... ) AS T` subquery), right after `poi."FormattedAddress",`.

Example for `public.spGetPointOfInterests.sql` (same edit pattern applies to the other two — `spGetPointOfInterestById.sql` only needs steps 1 and 2, since it selects directly from `poi` with no subquery):

```sql
CREATE OR REPLACE FUNCTION public.spGetPointOfInterest()
RETURNS TABLE (
    "RowId" BIGINT,
    "PointOfInterestId" INTEGER,
    "Container" VARCHAR(250),
    "OriginalFileName" VARCHAR(250),
    "GeneratedBlobName" VARCHAR(250),
    "Latitude" DOUBLE PRECISION,
    "Longitude" DOUBLE PRECISION,
    "DateCreated" TIMESTAMP WITH TIME ZONE,
    "DateTaken" TIMESTAMP WITH TIME ZONE,
    "FormattedAddress" VARCHAR(300),
    "Description" TEXT,
    "ImageResized" BOOLEAN,
    "TagId" INTEGER,
    "TagName" VARCHAR(50),
    "PointOfInterestKey" VARCHAR(40)
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        ROW_NUMBER() OVER (ORDER BY T."PointOfInterestId") AS "RowId",
        T."PointOfInterestId",
        T."Container",
        T."OriginalFileName",
        T."GeneratedBlobName",
        T."Latitude",
        T."Longitude",
        T."DateCreated",
        T."DateTaken",
        T."FormattedAddress",
        T."Description",
        T."ImageResized",
        T."TagId",
        T."TagName",
        T."PointOfInterestKey"
    FROM (
        SELECT
            poi."Id" AS "PointOfInterestId",
            poi."Container",
            poi."OriginalFileName",
            poi."GeneratedBlobName",
            poi."Latitude",
            poi."Longitude",
            poi."DateCreated",
            poi."DateTaken",
            poi."FormattedAddress",
            poi."Description",
            poi."ImageResized",
            t."Id" AS "TagId",
            t."Name" AS "TagName",
            poi."PointOfInterestKey",
            ROW_NUMBER() OVER (
                PARTITION BY poi."PointOfInterestKey"
                ORDER BY poi."DateCreated" DESC
            ) AS "ROW_NUM"
        FROM public."PointOfInterests" poi
        LEFT JOIN public."PointOfInterestTagAssociations" ita
            ON ita."PointOfInterestId" = poi."Id"
        LEFT JOIN public."Tags" t
            ON t."Id" = ita."TagId"
    ) AS T
    WHERE T."ROW_NUM" = 1
    ORDER BY T."DateCreated";
END;
$$ LANGUAGE plpgsql;
```

Apply the equivalent 3-line addition to `public.spGetPointOfInterestByTagName.sql` (identical subquery shape), and the equivalent 2-line addition (no subquery) to `public.spGetPointOfInterestById.sql`.

- [ ] **Step 7: Generate the EF migration**

```bash
cd src/common/mytravels.domain
dotnet ef migrations add AddPointOfInterestDescription --startup-project ../mytravels.migration --context CoreDbContext
```

This produces `AddColumn<string>(name: "Description", table: "PointOfInterests", type: "text", nullable: true)` in `Up()`, generated `.Designer.cs`, and an updated `CoreDbContextModelSnapshot.cs`. Do not hand-edit these three generated files beyond Step 8 below.

- [ ] **Step 8: Make the migration also re-apply the updated stored functions**

EF only diffs the C# model — it does not know the `.sql` files changed, and migrations are one-shot (already-applied ones don't re-run). Follow the exact precedent set by `20590930075747_UpdateStoredProcedure.cs`: add the same directory-scan-and-execute block to the bottom of the *new* migration's `Up()`, after the generated `AddColumn` call, so the edited functions from Step 6 actually reach the database:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PointOfInterests",
                type: "text",
                nullable: true);

            string baseDir = AppContext.BaseDirectory;
            var scriptsDir = Path.Combine(baseDir, "Features");
            string[] files = Directory.GetFiles(scriptsDir, "*.sql", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string script = File.ReadAllText(file);
                migrationBuilder.Sql(script);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException();
        }
```

(`CREATE OR REPLACE FUNCTION` is idempotent, so re-running all `.sql` files — including the two that didn't change — is safe, matching what `UpdateStoredProcedure` already does.)

- [ ] **Step 9: Verify it builds and the migration is well-formed**

```bash
dotnet build src/mytravels.sln
```

Expected: build succeeds, no EF warnings about a pending model change (i.e. the snapshot matches the model).

- [ ] **Step 10: Commit**

```bash
git add src/common/mytravels.contract/Entities/PointOfInterest.cs \
        src/common/mytravels.contract/Dtos/PointOfInterestDto.cs \
        src/common/mytravels.contract/Responses/GetPointOfInterestResponse.cs \
        src/api/mytravels.api/Extensions/ExtensionsMethods.cs \
        src/common/mytravels.domain/CoreDbContext.cs \
        src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterests.sql \
        src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterestById.sql \
        src/common/mytravels.domain/Features/PointOfInterest/public.spGetPointOfInterestByTagName.sql \
        src/common/mytravels.domain/Migrations/
git commit -m "feat: add PointOfInterest.Description column and wire it through the read path"
```

---

### Task 2: Publish on create; delete the synchronous describe path

**Files:**
- Modify: `src/common/mytravels.contract/Constants/ExchangeNames.cs`
- Modify: `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs`
- Modify: `src/common/mytravels.contract/Interfaces/IPointOfInterestService.cs`
- Modify: `src/api/mytravels.api/Controllers/PointOfInterestController.cs`

**Interfaces:**
- Consumes: `ExchangeNames.ResizeImage`, `ExchangeNames.AppendFormattedAddress` pattern (`ExchangeNames.cs:5-6`); `IMessagePublisher.PublishAsync` (already injected into `PointOfInterestService` as `_publisher`).
- Produces: `ExchangeNames.AppendImageTags = "append-image-tags"` — consumed by Task 3's new subscriber.

- [ ] **Step 1: Add the exchange name**

Edit `src/common/mytravels.contract/Constants/ExchangeNames.cs`:

```csharp
namespace mytravels.contract.Constants;

public static class ExchangeNames
{
    public const string AppendFormattedAddress = "append-formatted-address";
    public const string AppendImageTags = "append-image-tags";
    public const string ResizeImage = "resize-image";
}
```

- [ ] **Step 2: Publish the third message on creation**

Edit `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs`, in `CreatePointOfInterestAsync` (currently lines 127-128):

```csharp
            await _publisher.PublishAsync(ExchangeNames.AppendFormattedAddress, new PointOfInterestMessage { CorrelationId = Guid.NewGuid(), PointOfInterestId = id }, cancellationToken);
            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);
            await _publisher.PublishAsync(ExchangeNames.AppendImageTags, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);
```

- [ ] **Step 3: Remove `DescribeImageAsync` from the service**

Edit `src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs` — delete the whole method (currently lines 99-110):

```csharp
        public async Task<ImageDescriptionDto> DescribeImageAsync(int id, CancellationToken cancellationToken)
        {
            contract.Entities.PointOfInterest point = await _context.PointOfInterests
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new DataNotFoundException($"Point of interest with id '{id}' was not found");

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName))
                throw new ApiException(400, "This point of interest has no image to describe yet.");

            string base64 = await _objectStorageService.GetBase64Async(BucketNames.NewUploadedImagesContainer, point.GeneratedBlobName, cancellationToken);
            return await _imageDescriptionService.DescribeAsync(base64, cancellationToken);
        }
```

Leave `_imageDescriptionService` (the field and constructor injection) in place — `IImageDescriptionService` is a constructor dependency of `PointOfInterestService`, and `PointOfInterestService` is also what the new messaging subscriber resolves from its DI scope (Task 3), so it's still needed there. It's simply unused by `api`'s copy of this class after this change, which is fine — DI doesn't care.

- [ ] **Step 4: Remove `DescribeImageAsync` from the interface**

Edit `src/common/mytravels.contract/Interfaces/IPointOfInterestService.cs` — delete:

```csharp
    Task<ImageDescriptionDto> DescribeImageAsync(int id, CancellationToken cancellationToken);
```

`ImageDescriptionDto` and `IImageDescriptionService` themselves are untouched — Task 3's subscriber still uses both.

- [ ] **Step 5: Remove the controller action**

Edit `src/api/mytravels.api/Controllers/PointOfInterestController.cs` — delete (currently lines 61-67):

```csharp
        [HttpPost("{id:int}/describe")]
        [ProducesResponseType(typeof(ImageDescriptionDto), 200)]
        public async Task<IActionResult> DescribeImageAsync([FromRoute] int id, CancellationToken cancellationToken)
        {
            ImageDescriptionDto dto = await _service.DescribeImageAsync(id, cancellationToken);
            return Ok(dto);
        }
```

- [ ] **Step 6: Verify it builds**

```bash
dotnet build src/mytravels.sln
```

Expected: succeeds. (Confirmed during research: `DescribeImageAsync` has exactly these three call sites — service, interface, controller — and no `mytravels.mcp` tool calls it, so no other file needs touching.)

- [ ] **Step 7: Commit**

```bash
git add src/common/mytravels.contract/Constants/ExchangeNames.cs \
        src/common/mytravels.domain/Features/PointOfInterest/PointOfInterestService.cs \
        src/common/mytravels.contract/Interfaces/IPointOfInterestService.cs \
        src/api/mytravels.api/Controllers/PointOfInterestController.cs
git commit -m "feat: publish append-image-tags on POI creation; remove synchronous describe endpoint"
```

---

### Task 3: `AppendImageTags` messaging subscriber

**Files:**
- Create: `src/messaging/mytravels.messaging/AppendImageTags.cs`
- Modify: `src/messaging/mytravels.messaging/Program.cs`

**Interfaces:**
- Consumes: `ExchangeNames.AppendImageTags` (Task 2); `PointOfInterest.Description` (Task 1); `ICoreDbContext.UpdatePointOfInterestTagsAsync(List<SavePointOfInterestDto>, CancellationToken)` (`ICoreDbContext.cs:29`); `SavePointOfInterestDto { int PointOfInterestId; List<string> Tags; }`; `IImageDescriptionService.DescribeAsync(string base64Image, CancellationToken) -> ImageDescriptionDto { string Description; List<string> Tags; }`; `IObjectStorageService.GetBase64Async(string bucket, string blobName, CancellationToken)`; `BucketNames.NewUploadedImagesContainer`.
- Produces: nothing consumed elsewhere — this is a leaf subscriber.

- [ ] **Step 1: Write the subscriber**

Create `src/messaging/mytravels.messaging/AppendImageTags.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.domain;

namespace mytravels.functions;

public class AppendImageTags : MessageSubscriberBase<PointOfInterestMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public AppendImageTags
        (
            ILogger<AppendImageTags> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTags)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    protected override async Task ProcessMessageAsync(PointOfInterestMessage obj, CancellationToken cancellationToken)
    {
        if (obj is null) return;
        try
        {
            await semaphore.WaitAsync();
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreDbContext context = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();
            IObjectStorageService objectStorageService = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
            IImageDescriptionService imageDescriptionService = scope.ServiceProvider.GetRequiredService<IImageDescriptionService>();

            PointOfInterest point = await context.PointOfInterests.FirstOrDefaultAsync(x => x.Id == obj.PointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {obj.PointOfInterestId}");
            }

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName))
            {
                return;
            }

            string base64 = await objectStorageService.GetBase64Async(BucketNames.NewUploadedImagesContainer, point.GeneratedBlobName, cancellationToken);
            ImageDescriptionDto result = await imageDescriptionService.DescribeAsync(base64, cancellationToken);

            point.Description = result.Description;
            var entry = context.Entry(point);
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(point.Description)).IsModified = true;
            await context.SaveChangesAsync(cancellationToken);

            if (result.Tags.Count > 0)
            {
                await context.UpdatePointOfInterestTagsAsync(
                    new List<SavePointOfInterestDto>
                    {
                        new SavePointOfInterestDto { PointOfInterestId = point.Id, Tags = result.Tags }
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AppendImageTags message");
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

Notes on choices made here, matching established sibling-subscriber conventions:
- The `GeneratedBlobName` guard mirrors the old synchronous check, but `return`s instead of throwing `ApiException(400, ...)` — there is no HTTP caller to receive that response here, and a POI legitimately created before its image finished uploading (unlikely, but the guard existed for a reason) should not enter a nack/requeue retry loop forever. This mirrors `ResizeImage.cs`'s `if (point.ImageResized) return;` early-out style.
- try/catch/logging follows `AppendFormattedAddress.cs`'s pattern (log then rethrow, letting `MessageSubscriberBase` nack+requeue), not `ResizeImage.cs`'s (no catch, letting the base class's own catch handle it) — either is consistent with an existing sibling; this plan picks `AppendFormattedAddress`'s because this subscriber, like that one, calls an external API that can fail transiently (Claude), so an explicit log line before the requeue is useful for diagnosing repeated failures.
- Tags are only written `if (result.Tags.Count > 0)` — `UpdatePointOfInterestTagsAsync` already no-ops on an empty list (`CoreDbContext.cs:72`), so this is purely to avoid an unnecessary round trip when Claude returns no tags.

- [ ] **Step 2: Register the hosted service**

Edit `src/messaging/mytravels.messaging/Program.cs` — add alongside the other two:

```csharp
builder.Services.AddHostedService<AppendFormattedAddress>();
builder.Services.AddHostedService<AppendFormattedAddressSweeper>();
builder.Services.AddHostedService<ResizeImage>();
builder.Services.AddHostedService<AppendImageTags>();
```

No other DI registration is needed — `IImageDescriptionService → AnthropicImageDescriptionService` and `IPointOfInterestService → PointOfInterestService` are already registered (`Program.cs:62-63`), and `AppendImageTags` resolves `IImageDescriptionService`, `ICoreDbContext`, `IObjectStorageService` directly from its own DI scope like its siblings do — it does not go through `IPointOfInterestService` at all (there is no `DescribeImageAsync` left to call after Task 2).

- [ ] **Step 3: Verify it builds**

```bash
dotnet build src/mytravels.sln
```

Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/messaging/mytravels.messaging/AppendImageTags.cs src/messaging/mytravels.messaging/Program.cs
git commit -m "feat: add AppendImageTags messaging subscriber"
```

---

### Task 4: Frontend — render `Description`/`Tags` from POI data, remove the Describe button

**Files:**
- Modify: `src/web/src/api/types.ts`
- Modify: `src/web/src/api/client.ts`
- Modify: `src/web/src/components/PoiDialog.tsx`

**Interfaces:**
- Consumes: `PointOfInterestDto.Description` (Task 1) as JSON-serialized `description: string | null`; existing `PointOfInterest.tags: Tag[]` (`types.ts:7-16`, `Tag { id: number; name: string; dateCreated: string }`) — already populated by `UpdatePointOfInterestTagsAsync` (Task 3's write path), no new plumbing needed for tags to reach the frontend.

- [ ] **Step 1: Update types**

Edit `src/web/src/api/types.ts`:

```typescript
export interface PointOfInterest {
  id: number;
  pointOfInterestKey: string;
  latitude: number;
  longitude: number;
  dateCreated: string;
  dateTaken: string | null;
  formattedAddress: string;
  description: string | null;
  tags: Tag[];
}
```

Remove the now-unused `ImageDescription` interface (its only two consumers — `client.ts`'s `describePointOfInterestImage` and `PoiDialog.tsx`'s `DescribeState` — are both deleted in this task):

```typescript
export interface ImageDescription {
  description: string;
  tags: string[];
}
```

- [ ] **Step 2: Remove the describe API call**

Edit `src/web/src/api/client.ts` — remove the `ImageDescription` import and the function:

```typescript
import type { ImageDescription, Place, PointOfInterest, SaveEntityResponse } from './types';
```
becomes
```typescript
import type { Place, PointOfInterest, SaveEntityResponse } from './types';
```

Delete:

```typescript
export async function describePointOfInterestImage(
  id: number,
  signal?: AbortSignal,
): Promise<ImageDescription> {
  const response = await fetch(`${BASE_URL}/api/PointOfInterest/${id}/describe`, {
    method: 'POST',
    signal,
  });
  return unwrap<ImageDescription>(response);
}
```

- [ ] **Step 3: Rewrite `PoiDialog.tsx`**

Edit `src/web/src/components/PoiDialog.tsx`. Remove the `ImageDescription` import and the `describePointOfInterestImage` import; remove the `DescribeState` type; remove the `describe` state, its `handleDescribe` handler, and the button/error/described blocks; render `poi.description` and `poi.tags` directly.

```typescript
import { useEffect, useState } from "react";
import type { PointOfInterest } from "../api/types";
import { getPointOfInterestImage } from "../api/client";
import { Spinner } from "./Spinner";
import { PostmarkGlyph } from "./icons";

interface PoiDialogProps {
  poi: PointOfInterest;
  onClose: () => void;
}

function formatDate(value: string) {
  const date = new Date(value);
  const weekday = date.toLocaleDateString("en-GB", { weekday: "long" });
  const day = date.getDate();
  const month = date.toLocaleDateString("en-GB", { month: "long" });
  const year = date.getFullYear();
  const time = date.toLocaleTimeString("en-US", {
    hour: "numeric",
    minute: "2-digit",
    hour12: true,
  });
  return `${weekday}, ${day} ${month} ${year} at ${time}`;
}

type ImageState =
  | { status: "loading" }
  | { status: "loaded"; src: string }
  | { status: "error" };

function ImagePlaceholderIcon() {
  return (
    <svg
      className="h-12 w-12 text-brass/40 dark:text-brass/30"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <circle cx="8.5" cy="8.5" r="1.5" />
      <path d="M21 15l-5-5L5 21" />
    </svg>
  );
}

export function PoiDialog({ poi, onClose }: PoiDialogProps) {
  const [image, setImage] = useState<ImageState>({ status: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    setImage({ status: "loading" });

    getPointOfInterestImage(poi.id, true, controller.signal)
      .then((base64) =>
        setImage({ status: "loaded", src: `data:image/jpeg;base64,${base64}` }),
      )
      .catch((err) => {
        if (err.name !== "AbortError") setImage({ status: "error" });
      });

    return () => controller.abort();
  }, [poi.id]);

  return (
    <div
      className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-brass/30 bg-paper shadow-xl dark:border-brass/25 dark:bg-harbor-2"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="p-2">
          <div className="relative flex aspect-video w-full items-center justify-center overflow-hidden rounded-sm bg-ink/5 ring-1 ring-brass/25 dark:bg-bone/5">
            {image.status === "loaded" ? (
              <img
                src={image.src}
                alt={poi.formattedAddress}
                className="h-full w-full object-cover"
              />
            ) : (
              <ImagePlaceholderIcon />
            )}
            {image.status === "loading" && (
              <div className="absolute inset-0 flex items-center justify-center bg-paper/60 dark:bg-harbor-2/60">
                <Spinner className="h-6 w-6 text-brass" />
              </div>
            )}
          </div>
        </div>

        <div className="space-y-4 px-5 pt-1 pb-5">
          {(poi.description || poi.tags.length > 0) && (
            <div className="space-y-2 border-t border-brass/20 pt-3">
              {poi.description && (
                <p className="font-sans text-sm text-ink dark:text-bone">{poi.description}</p>
              )}
              {poi.tags.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {poi.tags.map((tag) => (
                    <span
                      key={tag.id}
                      className="rounded-full border border-brass/40 bg-brass/10 px-2.5 py-0.5 font-mono text-[10px] tracking-wide text-ink uppercase dark:border-brass/30 dark:bg-brass/10 dark:text-bone"
                    >
                      {tag.name}
                    </span>
                  ))}
                </div>
              )}
            </div>
          )}

          <div className="flex items-start justify-between gap-2">
            <h2 className="font-display text-lg font-medium text-ink dark:text-bone">
              {poi.formattedAddress || "Address pending"}
            </h2>
            <button
              type="button"
              onClick={onClose}
              className="text-brass transition hover:text-postmark dark:hover:text-postmark-light"
              aria-label="Close"
            >
              ✕
            </button>
          </div>

          <div className="flex items-center gap-3 border-t border-brass/20 pt-4">
            <div className="flex h-10 w-10 shrink-0 -rotate-6 items-center justify-center rounded-full border border-dashed border-postmark/60 text-postmark dark:border-postmark-light/60 dark:text-postmark-light">
              <PostmarkGlyph className="h-5 w-5" />
            </div>
            <div>
              <p className="font-mono text-[10px] tracking-[0.16em] text-brass uppercase">
                Taken
              </p>
              <p className="font-sans text-sm text-ink dark:text-bone">
                {formatDate(poi.dateTaken ?? poi.dateCreated)}
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
```

(`poi.tags` was already declared on `PointOfInterest` and already populated by the API before this change — it just had no renderer in this dialog. This task adds one, using the same pill markup the old `describe.result.tags` block used, keyed by `tag.id` instead of the tag string since `Tag` is now an object.)

- [ ] **Step 4: Lint and build**

```bash
cd src/web
npm run lint
npm run build
```

Expected: both succeed with no errors (oxlint should flag nothing — no unused imports remain: `Spinner` is still used for the image-loading state, `PostmarkGlyph` still used, `ImageDescription` type is deleted at its only definition site).

- [ ] **Step 5: Manual browser check**

```bash
npm run dev
```

Open the app, click an existing POI with the dialog. Confirm: no "Describe" button appears anywhere; if that POI already has `description`/`tags` data (e.g. seeded before this change, or after Task 3 has run against it), it renders above the address; if not, the dialog just shows image + address + taken-date as before, with no error state.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/api/types.ts src/web/src/api/client.ts src/web/src/components/PoiDialog.tsx
git commit -m "feat: render description/tags from POI data, remove manual Describe button"
```

---

### Task 5: Infra — stages 1 & 2 (Compose): move `AnthropicApiKey` from `api` to `messaging`

**Files:**
- Modify: `1-dockerize/docker-compose.yml`
- Modify: `2-dockerhub/docker-compose.yml`

**Interfaces:**
- Consumes: `ANTHROPIC_API_KEY` from `.env`/`.env.example` in each stage directory — unchanged, still read by `AnthropicImageDescriptionService(IConfiguration)` via the env var name `AnthropicApiKey` (Task 3's subscriber lives in the same `messaging` process/DI container that already registers that service).

- [ ] **Step 1: `1-dockerize/docker-compose.yml`**

Remove the line from the `api` service's `environment:` block (currently line 113):

```yaml
      AnthropicApiKey: ${ANTHROPIC_API_KEY}
```

Add it to the `messaging` service's `environment:` block (after `RabbitMQ__Uri: ${RABBIT_MQ_URI}`, alongside `GoogleApiKey`/`GoogleMapsUrl`/`GooglePlacesUrl` which are already there):

```yaml
      RabbitMQ__Uri: ${RABBIT_MQ_URI}
      AnthropicApiKey: ${ANTHROPIC_API_KEY}
      MinIO__Endpoint: ${MINIO_ENDPOINT}
```

- [ ] **Step 2: `2-dockerhub/docker-compose.yml`**

Same two edits: remove `AnthropicApiKey: ${ANTHROPIC_API_KEY}` from the `api` service (currently line 105), add it to the `messaging` service's `environment:` block.

`.env`/`.env.example` in both stages already declare `ANTHROPIC_API_KEY=<YOUR_ANTHROPIC_API_KEY>` — no changes needed there, it's just consumed by a different container now.

- [ ] **Step 3: Verify with a config render (no cluster/daemon needed)**

```bash
cd 1-dockerize && docker compose config | grep -A2 -B12 AnthropicApiKey
cd ../2-dockerhub && docker compose config | grep -A2 -B12 AnthropicApiKey
```

Expected: in both, `AnthropicApiKey` appears once, under the `messaging` service, not under `api`. (Requires `.env` to exist in each directory — copy from `.env.example` first if it doesn't; do not commit a populated `.env`.)

- [ ] **Step 4: Commit**

```bash
git add 1-dockerize/docker-compose.yml 2-dockerhub/docker-compose.yml
git commit -m "chore: move AnthropicApiKey wiring from api to messaging (stages 1-2)"
```

---

### Task 6: Infra — stage 3 (`kubectl apply` manifests): move `AnthropicApiKey` secret + env

**Files:**
- Modify: `3-kubernetes/manifests/api/1-secret.yaml`
- Modify: `3-kubernetes/manifests/api/2-deployment.yaml`
- Modify: `3-kubernetes/manifests/messaging/1-secret.yaml`
- Modify: `3-kubernetes/manifests/messaging/2-deployment.yaml`

- [ ] **Step 1: Remove from `api`**

Edit `3-kubernetes/manifests/api/1-secret.yaml` — remove:

```yaml
  AnthropicApiKey: PFlPVVJfQU5USFJPUKLDRV9BUElfS0VZPg==
```

Edit `3-kubernetes/manifests/api/2-deployment.yaml` — remove the env entry (currently lines 56-60):

```yaml
            - name: AnthropicApiKey
              valueFrom:
                secretKeyRef:
                  name: api-secret
                  key: AnthropicApiKey
```

- [ ] **Step 2: Add to `messaging`**

Edit `3-kubernetes/manifests/messaging/1-secret.yaml` — add the same placeholder value (base64 of `<YOUR_ANTHROPIC_API_KEY>`) as a new key:

```yaml
  AnthropicApiKey: PFlPVVJfQU5USFJPUKLDRV9BUElfS0VZPg==
```

Edit `3-kubernetes/manifests/messaging/2-deployment.yaml` — add the env entry (after `MinIO__SecretKey`, before `GoogleApiKey`, matching the api deployment's ordering convention):

```yaml
            - name: MinIO__SecretKey
              valueFrom:
                secretKeyRef:
                  name: messaging-secret
                  key: MinIO__SecretKey
            - name: AnthropicApiKey
              valueFrom:
                secretKeyRef:
                  name: messaging-secret
                  key: AnthropicApiKey
            - name: GoogleApiKey
```

- [ ] **Step 3: Verify manifests are well-formed**

```bash
kubectl apply --dry-run=client -f 3-kubernetes/manifests/api/1-secret.yaml -f 3-kubernetes/manifests/api/2-deployment.yaml -f 3-kubernetes/manifests/messaging/1-secret.yaml -f 3-kubernetes/manifests/messaging/2-deployment.yaml
```

Expected: `configured`/`created` (client-side) for all four with no YAML errors. (If no cluster context is available, at minimum run `kubectl apply --dry-run=client -f <file> -o yaml` per file to confirm each parses — that requires no live cluster.)

- [ ] **Step 4: Commit**

```bash
git add 3-kubernetes/manifests/api/1-secret.yaml 3-kubernetes/manifests/api/2-deployment.yaml \
        3-kubernetes/manifests/messaging/1-secret.yaml 3-kubernetes/manifests/messaging/2-deployment.yaml
git commit -m "chore: move AnthropicApiKey secret/env from api to messaging (stage 3)"
```

---

### Task 7: Infra — stage 4 (Argo CD): move `AnthropicApiKey` deployment env + runbook secret creation

**Files:**
- Modify: `4-argocd/manifests/api/deployment.yaml`
- Modify: `4-argocd/manifests/messaging/deployment.yaml`
- Modify: `4-argocd/runbook.ipynb`
- Modify: `4-argocd/.env.example`

**Context:** stage 4 has no `*secret.yaml` manifests in git (`.gitignore` excludes `*secret.yaml` on purpose — see the "Namespace and Secrets" markdown cell in the runbook). Secrets are created imperatively by a notebook cell that reads `4-argocd/.env` and builds a `kubectl create secret generic` command per service. That cell — not a YAML file — is the actual place `AnthropicApiKey` is assigned to `api-secret` vs `messaging-secret` for this stage.

- [ ] **Step 1: `api/deployment.yaml`**

Edit `4-argocd/manifests/api/deployment.yaml` — remove the same env entry as stage 3 (currently lines 58-62):

```yaml
            - name: AnthropicApiKey
              valueFrom:
                secretKeyRef:
                  name: api-secret
                  key: AnthropicApiKey
```

- [ ] **Step 2: `messaging/deployment.yaml`**

Edit `4-argocd/manifests/messaging/deployment.yaml` — add, after `MinIO__SecretKey` and before `GoogleApiKey`:

```yaml
            - name: AnthropicApiKey
              valueFrom:
                secretKeyRef:
                  name: messaging-secret
                  key: AnthropicApiKey
```

- [ ] **Step 3: Update the runbook's secret-creation cell**

In `4-argocd/runbook.ipynb`, find the code cell that builds the `secrets` dict (contains `"api-secret": {**dict(shared), "AnthropicApiKey": env["ANTHROPIC_API_KEY"]},`). Change:

```python
    "api-secret": {**dict(shared), "AnthropicApiKey": env["ANTHROPIC_API_KEY"]},
    "messaging-secret": {
        **shared,
        "ContentSafetyEndpoint": env["CONTENT_SAFETY_ENDPOINT"],
        "ContentSafetyKey": env["CONTENT_SAFETY_KEY"],
    },
```

to:

```python
    "api-secret": dict(shared),
    "messaging-secret": {
        **shared,
        "AnthropicApiKey": env["ANTHROPIC_API_KEY"],
        "ContentSafetyEndpoint": env["CONTENT_SAFETY_ENDPOINT"],
        "ContentSafetyKey": env["CONTENT_SAFETY_KEY"],
    },
```

`ANTHROPIC_API_KEY` stays in the `need(...)` required-keys call a few lines above — it's still required, just for a different secret now.

- [ ] **Step 4: Update the runbook's secret-key documentation table**

In the same notebook, the preceding markdown cell has a table documenting each secret's keys. Update the two affected rows:

```markdown
| `api-secret` | `ConnectionStrings__CoreDbContext`, `RabbitMQ__Uri`, `MinIO__AccessKey`, `MinIO__SecretKey` |
| `messaging-secret` | the four above, plus `ContentSafetyEndpoint`, `ContentSafetyKey` |
```

becomes:

```markdown
| `api-secret` | `ConnectionStrings__CoreDbContext`, `RabbitMQ__Uri`, `MinIO__AccessKey`, `MinIO__SecretKey` |
| `messaging-secret` | the four above, plus `AnthropicApiKey`, `ContentSafetyEndpoint`, `ContentSafetyKey` |
```

- [ ] **Step 5: Update `.env.example` comments**

Edit `4-argocd/.env.example` — the `ANTHROPIC_API_KEY` line currently sits under a `# api-secret only` comment. Move it under the `# messaging-secret only` group instead:

```
# migrations-secret / api-secret / messaging-secret
CORE_DB_CONTEXT=Host=postgres;Port=5432;Database=CoreDb;Username=user123;Password=password123
RABBIT_MQ_URI=amqp://user123:password123@rabbitmq:5672

# messaging-secret only
ANTHROPIC_API_KEY=<YOUR_ANTHROPIC_API_KEY>
CONTENT_SAFETY_ENDPOINT=<YOUR_CONTENT_SAFETY_ENDPOINT>
CONTENT_SAFETY_KEY=<YOUR_CONTENT_SAFETY_KEY>

# grafana-secret
```

(the `# api-secret only` comment line is removed entirely, since after this change `api-secret` has no unique keys of its own beyond the shared four.)

- [ ] **Step 6: Validate the notebook cell edit is syntactically correct**

```bash
cd 4-argocd
jupyter nbconvert --to script --stdout runbook.ipynb 2>/dev/null | python3 -m py_compile /dev/stdin
```

Expected: no syntax errors. (This only checks the notebook's Python cells parse — it does not execute against a live cluster. Full execution happens via the `validate-runbook` skill or Task 9's manual pass.)

- [ ] **Step 7: Commit**

```bash
git add 4-argocd/manifests/api/deployment.yaml 4-argocd/manifests/messaging/deployment.yaml \
        4-argocd/runbook.ipynb 4-argocd/.env.example
git commit -m "chore: move AnthropicApiKey secret/env from api to messaging (stage 4)"
```

---

### Task 8: Runbooks — verify the automatic flow after upload

**Files:**
- Modify: `3-kubernetes/runbook.ipynb`
- Modify: `4-argocd/runbook.ipynb`

**Context:** the spec assumed both runbooks had an existing walkthrough step for the manual "Describe" button that needed removing. That's not the case — neither runbook mentions the describe endpoint or button anywhere (verified by search); the only "describe" hits are unrelated `kubectl describe pod` calls. So there is nothing to remove. What's missing is a verification step for the *new* automatic behavior, which this task adds right after each runbook's existing "Seed Test Data" step (the cell that uploads photos via `../.claude/scripts/upload-photos.sh` — cell 67 in `3-kubernetes/runbook.ipynb`, cell 57 in `4-argocd/runbook.ipynb`).

- [ ] **Step 1: Add a verification cell to `3-kubernetes/runbook.ipynb`**

Insert a new markdown cell after the existing "Step 18 — Seed Test Data" code cell:

```markdown
---

## Step 19 — Verify Automatic Description & Tagging

The photos just uploaded each triggered three parallel background jobs: thumbnail resize, reverse-geocoding, and (new) AI description/tagging via the `messaging` service's `append-image-tags` subscriber. Unlike the other two, this one calls out to the Anthropic API, so it can take a few seconds per photo. This cell polls one POI until `description` and `tags` appear, or times out and prints diagnostics inline.
```

Followed by a new code cell:

```python
%%bash
API_BASE="http://api.mytravels.local:8080"

POI_ID=$(curl -s "$API_BASE/api/pointofinterest" | python3 -c "import json,sys; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")

if [ -z "$POI_ID" ]; then
  echo "No points of interest found — run Step 18 first."
  exit 0
fi

echo "Polling POI $POI_ID for description/tags..."
for i in $(seq 1 12); do
  RESULT=$(curl -s "$API_BASE/api/pointofinterest" | python3 -c "
import json, sys
d = json.load(sys.stdin)
poi = next((p for p in d if p['id'] == $POI_ID), None)
if poi and poi.get('description'):
    print(f\"OK description={poi['description']!r} tags={[t['name'] for t in poi['tags']]}\")
")
  if [ -n "$RESULT" ]; then
    echo "$RESULT"
    exit 0
  fi
  sleep 5
done

echo "No description after 60s — diagnosing inline:"
echo "--- messaging pods ---"
kubectl get pods -n mytravels-default -l app=messaging -o wide
echo "--- messaging logs (last 40, filtered to AppendImageTags) ---"
kubectl logs -n mytravels-default -l app=messaging --tail=200 | grep -i "AppendImageTags\|append-image-tags" | tail -40
```

- [ ] **Step 2: Add the equivalent cell to `4-argocd/runbook.ipynb`**

Same two cells (markdown "Step 16 — Verify Automatic Description & Tagging" — renumber to fit after the existing "Step 15 — Seed Test Data"; renumber every subsequent step heading in the file by +1 accordingly), inserted after the seed-data code cell. Use the same polling code cell as Step 1, with one addition to the timeout branch — mirroring this runbook's existing pattern (seen in its own seed-data cell) of checking the Argo CD Application health before pod logs, since a failed sync there looks identical to a down service:

```python
echo "No description after 60s — diagnosing inline:"
echo "--- Application (a failed sync looks like a down messaging service) ---"
kubectl get application mytravels -n argocd
kubectl get application mytravels -n argocd \
  -o jsonpath='{.status.operationState.phase}: {.status.operationState.message}{"\n"}'
echo "--- messaging pods ---"
kubectl get pods -n mytravels-default -l app=messaging -o wide
echo "--- messaging logs (last 40, filtered to AppendImageTags) ---"
kubectl logs -n mytravels-default -l app=messaging --tail=200 | grep -i "AppendImageTags\|append-image-tags" | tail -40
```

- [ ] **Step 3: Renumber subsequent step headings**

Both runbooks number their `## Step N — ...` markdown headings sequentially. After inserting a new step, walk the remaining cells in each notebook and increment every subsequent step number by 1 (and any `#step<N>` anchor links that reference a renumbered step — `4-argocd/runbook.ipynb` has at least one, in the Seed Test Data markdown cell: `[Step 16](#step15)` referring to a later "delete the namespace" step).

- [ ] **Step 4: Validate both notebooks parse**

```bash
jupyter nbconvert --to script --stdout 3-kubernetes/runbook.ipynb > /dev/null
jupyter nbconvert --to script --stdout 4-argocd/runbook.ipynb > /dev/null
```

Expected: both exit 0 with no conversion errors. Full execution against a live cluster is the `validate-runbook` skill's job, not this step's — but if a cluster from an earlier stage run is available, running the new cell manually (`jupyter nbconvert --to notebook --execute`) after Task 3/9 are deployed is the strongest confirmation.

- [ ] **Step 5: Commit**

```bash
git add 3-kubernetes/runbook.ipynb 4-argocd/runbook.ipynb
git commit -m "docs: add automatic description/tagging verification step to runbooks"
```

---

### Task 9: Diagram — `drawio/architecture.drawio`

**Files:**
- Modify: `drawio/architecture.drawio`

**Context:** this is plain XML (mxGraph format), hand-editable. Two pages need the same addition: `<diagram name="Application Architecture" id="sGbywr-7uBfR10_49Oi6">` (starts at the file's `<diagram>` on line 2) and `<diagram id="AppArchMonitoringNew1" name="Monitoring">` (line 288) — the latter is a near-exact duplicate of the former with identical node IDs/geometry, so the same edit applies to both, verbatim.

The existing "Append Address Subscriber" group is the template to copy:

| Existing node | id | geometry (h, w, x, y) |
|---|---|---|
| `append-formatted-address` queue icon | `q0HhcgHjfF6fuH3oCPwb-15` | 60, 60, 1080, 1062 |
| `Append Address Subscriber :5102` box | `q0HhcgHjfF6fuH3oCPwb-19` | 60, 120, 1357, 1062 |
| edge: queue → subscriber | `q0HhcgHjfF6fuH3oCPwb-13` | source=`-15` target=`-19` |
| edge: subscriber → `CoreDB :5432` (`q0HhcgHjfF6fuH3oCPwb-12`) | `q0HhcgHjfF6fuH3oCPwb-22` | source=`-19` target=`-12` |
| edge: `API :5101` (`q0HhcgHjfF6fuH3oCPwb-8`) → queue | `q0HhcgHjfF6fuH3oCPwb-16` | target=`-15`, no explicit `source` (routed via waypoint) |

- [ ] **Step 1: Add the new queue + subscriber + Anthropic API nodes to the "Application Architecture" page**

Insert these four `mxCell` blocks anywhere inside `<root>...</root>` of the first `<diagram>` (e.g. right after the existing `q0HhcgHjfF6fuH3oCPwb-19` block), offset 140px below the Append Address row (`y=1062` → `y=1202`) so the three subscriber rows stack cleanly:

```xml
        <mxCell id="aitags-1" parent="1" style="sketch=0;outlineConnect=0;fontColor=#232F3E;gradientColor=none;strokeColor=#232F3E;fillColor=#ffffff;dashed=0;verticalLabelPosition=top;verticalAlign=bottom;align=center;html=1;fontSize=12;fontStyle=0;aspect=fixed;shape=mxgraph.aws4.resourceIcon;resIcon=mxgraph.aws4.queue;labelPosition=center;strokeWidth=3;" value="&lt;b&gt;append-image-tags&lt;/b&gt;" vertex="1">
          <mxGeometry height="60" width="60" x="1080" y="1202" as="geometry" />
        </mxCell>
        <mxCell id="aitags-2" parent="1" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#eeeeee;strokeColor=#36393d;strokeWidth=3;" value="&lt;b&gt;Append Image Tags&lt;/b&gt;&lt;div&gt;&lt;b&gt;Subscriber&lt;/b&gt;&lt;/div&gt;&lt;div&gt;:5102&lt;/div&gt;" vertex="1">
          <mxGeometry height="60" width="120" x="1357" y="1202" as="geometry" />
        </mxCell>
        <mxCell id="aitags-3" parent="1" style="shape=image;verticalLabelPosition=bottom;labelBackgroundColor=default;verticalAlign=top;aspect=fixed;imageAspect=0;html=1;fillColor=#eeeeee;strokeColor=#36393d;" value="&lt;b&gt;Anthropic Claude API&lt;/b&gt;" vertex="1">
          <mxGeometry height="60" width="120" x="1580" y="1202" as="geometry" />
        </mxCell>
        <mxCell id="aitags-4" edge="1" parent="1" source="aitags-1" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;entryX=0;entryY=0.5;entryDx=0;entryDy=0;strokeColor=#808080;strokeWidth=3;" target="aitags-2">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="aitags-5" edge="1" parent="1" source="aitags-2" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;strokeColor=#808080;strokeWidth=3;" target="q0HhcgHjfF6fuH3oCPwb-12">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="aitags-6" edge="1" parent="1" source="aitags-2" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;strokeColor=#808080;strokeWidth=3;" target="aitags-3">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
        <mxCell id="aitags-7" edge="1" parent="1" source="q0HhcgHjfF6fuH3oCPwb-8" style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;strokeColor=#808080;strokeWidth=3;" target="aitags-1">
          <mxGeometry relative="1" as="geometry" />
        </mxCell>
```

This mirrors the existing pattern exactly: `API :5101` (`-8`) fans out to the new queue (`aitags-7`, matching how `-8` already fans out to the `resize-image` and `append-formatted-address` queues via `-7` and `-16`); the queue feeds the new subscriber box (`aitags-4`, matching `-13`/`-17`); the subscriber writes to `CoreDB` (`aitags-5`, matching `-22`/`-21`); and the subscriber additionally calls out to a new "Anthropic Claude API" box (`aitags-6`) — this edge has no precedent among the other two subscribers since neither calls an external API, so it's the one genuinely new relationship in the diagram, which is also why the spec calls out that Anthropic was never depicted at all before this change.

- [ ] **Step 2: Repeat Step 1 verbatim on the "Monitoring" page**

The "Monitoring" page (`<diagram id="AppArchMonitoringNew1" name="Monitoring">`, starting line 288) duplicates the same node IDs and geometry as the Application Architecture page for the shared elements. Insert the identical seven-block snippet from Step 1 into this page's `<root>` as well — reuse the same `aitags-*` local ids (mxGraph diagram IDs are scoped per `<diagram>`, so reusing them across pages is fine and matches how `q0HhcgHjfF6fuH3oCPwb-*` ids are already reused verbatim between both pages).

Additionally, per the spec, mirror this page's existing OTLP/telemetry arrow pattern for the two existing subscribers (search this page for any additional edge/label cells connecting `-10`/`-19` to a telemetry/collector node, and add the equivalent for the new subscriber box `aitags-2`) — inspect the Monitoring page around the existing subscriber boxes to find that pattern before replicating it, since it wasn't part of the shared node set captured above.

- [ ] **Step 3: Open in draw.io (or the VS Code drawio extension) and eyeball it**

There is no automated validator for this file. Open `drawio/architecture.drawio`, check both pages: three subscriber rows now line up under `API :5101`/`CoreDB`, the new queue/subscriber/Anthropic boxes don't overlap the dashed grouping rectangles already on the canvas (`q0HhcgHjfF6fuH3oCPwb-1` through `-5` etc.), and labels read correctly. Nudge `x`/`y` by hand in the tool if anything overlaps — the exact pixel position is not load-bearing, only that the new elements are legible and don't collide.

- [ ] **Step 4: Commit**

```bash
git add drawio/architecture.drawio
git commit -m "docs: add Append Image Tags subscriber to architecture diagrams"
```

---

### Task 10: End-to-end verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

```bash
dotnet build src/mytravels.sln
```

Expected: succeeds with no errors.

- [ ] **Step 2: Web build + lint**

```bash
cd src/web && npm run lint && npm run build
```

Expected: succeeds with no errors.

- [ ] **Step 3: Live run via stage 1 (Compose)**

```bash
cd 1-dockerize
cp -n .env.example .env   # if .env doesn't already exist — then fill in a real ANTHROPIC_API_KEY
docker compose up --build -d
```

Wait for `api`, `messaging`, `postgres`, `rabbitmq`, `minio` to report healthy (`docker compose ps`).

- [ ] **Step 4: Upload a geotagged photo and confirm the automatic flow**

```bash
curl -F "image=@/path/to/a/geotagged/photo.jpg" http://localhost:5101/api/pointofinterest/image
```

Note the returned `id`, then poll:

```bash
for i in $(seq 1 12); do
  curl -s http://localhost:5101/api/pointofinterest | python3 -c "
import json, sys
d = json.load(sys.stdin)
poi = next((p for p in d if p['id'] == <ID>), None)
print(poi.get('description'), [t['name'] for t in poi['tags']] if poi else 'not found')
"
  sleep 5
done
```

Expected: within ~60s, `description` is a non-null sentence and `tags` is a non-empty list of lowercase words. If it stays null, check `docker compose logs messaging | grep -i AppendImageTags` for the failure (most likely cause during manual testing: `ANTHROPIC_API_KEY` still holds the `.env.example` placeholder).

- [ ] **Step 5: Confirm the old endpoint is gone**

```bash
curl -i -X POST http://localhost:5101/api/pointofinterest/<ID>/describe
```

Expected: `404 Not Found` (route no longer exists), not `400`/`503` (which would mean the controller action is still there).

- [ ] **Step 6: Confirm the UI**

Open `http://localhost` (or wherever `web` is mapped), open the dialog for the uploaded POI, confirm the description paragraph and tag pills render, and confirm there is no "Describe" button anywhere in the dialog.

- [ ] **Step 7: Tear down**

```bash
docker compose down
```

- [ ] **Step 8: Report status**

No commit — this task is verification-only. If any step fails, return to the relevant earlier task, fix, and re-run this task from Step 3.

---

## Self-Review Notes

- **Spec coverage:** every numbered item in the spec's "Changes" section maps to a task above (contract/domain → Task 1+2, API → Task 2, messaging → Task 3, frontend → Task 4, infra stages 1-2 → Task 5, stage 3 → Task 6, stage 4 → Task 7, runbooks → Task 8, diagrams → Task 9). The spec's "Result delivery" design decision (#2 — no polling endpoint, dialog reads from the POI list it already has) required no dedicated task since it falls out of Task 1 (DTO field) + Task 4 (render it) with no new endpoint.
- **Corrected two spec inaccuracies** (flagged inline where they matter): the `UpdatePointOfInterestTagsAsync` parameter type (Global Constraints + Task 3), and the runbooks not actually containing a describe-button walkthrough to remove (Task 8 adds a verification step instead of replacing a nonexistent one).
- **Filled one spec gap:** the stored-function (`*.sql`) changes needed for `Description` to actually flow through `GET api/PointOfInterest`, which the spec's "New EF Core migration" bullet did not mention (Task 1, Steps 6 and 8).
