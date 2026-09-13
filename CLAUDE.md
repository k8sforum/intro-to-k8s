# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Kubernetes learning course (KCNA/CKAD-oriented) built around one running example app, **MyTravels** (a .NET 10 / React geolocation Points-of-Interest app, source in `src/`). Five stages under the repo root deploy the *same* app with progressively more sophisticated tooling — each stage is a self-contained lesson:

| Stage | Directory | Adds |
|---|---|---|
| 0 | `0-local/` | Infra only (Postgres/RabbitMQ/MinIO/SOLR) via Compose; app run from source |
| 1 | `1-dockerize/` | Full stack containerized, built locally via Compose; adds the observability stack |
| 2 | `2-dockerhub/` | Images built & pushed to Docker Hub, stack runs from registry images (no observability) |
| 3 | `3-kubernetes/` | Deployed to a k3d cluster via raw `kubectl apply` manifests + Traefik ingress |
| 4 | `4-argocd/` | Same manifests, GitOps-deployed via Argo CD (sync waves, drift/self-heal) |

The app deploys five units: `api` (5101), `messaging` (5102), `mcp` (5103), `web`, and `migration` (a one-shot job), on top of the shared libraries under `src/common/`.

Each stage directory has its own `.env`/`.env.example`, `docker-compose.yml` (**stages 0–2 only** — stages 3 and 4 are manifests, no Compose file) and a **`runbook.ipynb`** — the canonical hands-on walkthrough for that stage; treat it as the primary doc, not the `.md` files.

**`SPEC.md`** at the repo root is a full reverse-engineered technical spec (architecture, APIs, data model, config surface, and a catalogued list of known bugs/dead code/fragile logic). Read it before making non-trivial changes to `src/` instead of re-deriving architecture from scratch.

## Commands

**.NET (`src/`, solution: `src/mytravels.sln`)**
```bash
dotnet build src/mytravels.sln
cd src/api/mytravels.api && dotnet run          # API, port 5101, Swagger at /swagger
cd src/messaging/mytravels.messaging && dotnet run  # background worker, port 5102
cd src/api/mytravels.mcp && dotnet run          # MCP server, port 5103, health at /health
cd src/common/mytravels.migration && dotnet run     # apply EF Core migrations
```

**Web (`src/web/`)**
```bash
npm run dev      # vite dev server
npm run build    # tsc -b && vite build
npm run lint     # oxlint
```

**Per-stage stack** (from inside the stage directory, e.g. `1-dockerize/`)
```bash
docker compose up --build
```

**No automated test suite exists anywhere in this repo** (no test projects in the `.sln`, no test script in `package.json`) — don't go looking for one.

## Architecture (brief — see `SPEC.md` §1–2 for full detail)

```
web (React/nginx) → api (5101) ──► PostgreSQL
   /  (map)             │   │        ▲  (POIs + MessageAuditLogs)
   /traceability        │   └──────► SOLR (8983) ◄── index/reindex
                        ▼ publish    ▲    (POI search: address + tags + description)
                    RabbitMQ ──► messaging worker (5102) ──► MinIO (S3) / PostgreSQL
                       ▲                     └──► Anthropic (description/tags)
mcp (5103) ────────────┘  (MCP tool server — same domain services as api, no REST surface)
```

`.NET dependency graph`: `api`, `messaging`, and `mcp` all depend on `common`, `domain`, and `storage`, which all depend on `contract`; `migration` depends on `common`, `domain`, and `contract` (no `storage`, no S3/RabbitMQ concerns — it only needs EF Core and the domain model). `web` is a fully separate npm project with no dependency on the C# code — it talks to the API only over REST (`src/web/src/api/client.ts`).

Four RabbitMQ fanout exchanges drive async work, each consumed by its own subscriber in `src/messaging/mytravels.messaging/`: `resize-image` (thumbnail generation), `append-formatted-address` (Google Maps/OSM geocoding, with a periodic sweeper retrying failures), `append-image-tags` (Claude-generated description/tags — see below), and `index-solr` (SOLR indexing — see below). Three of them are published on POI creation (`PointOfInterestService.CreatePointOfInterestAsync`, `src/common/mytravels.domain/`); **`append-image-tags` is the exception — it is chained off the tail of `ResizeImage`**, so the description and tags are generated once per image and the `index-solr` message carrying them is published only after the Anthropic call succeeded. A fifth, `reindex-solr`, is published only by `POST /api/pointofinterest/reindex`. Each has a paired `<name>-failed` fanout exchange (declared up front by `FailedExchangeDeclarer`) that receives a `FailedMessage` after three failed attempts — see the traceability note below.

**`mytravels.mcp` (`src/api/mytravels.mcp/`)** is easy to miss — it sits under `src/api/` next to the REST API. It exposes two MCP tools over streamable HTTP via `MapMcp()`: `search_place` (place lookup for photos with no GPS metadata) and `search_pointofinterest` (SOLR-backed search over saved POIs' address, tags and description — the free-text argument is `term`, not `query`, plus optional `tag`, `from` and `to`). It reuses `IMapsService`/`IPointOfInterestService` directly rather than calling the REST API, so **a change to those services' search behaviour affects both `api` and `mcp`** — check `src/api/mytravels.mcp/Tools/` whenever you touch them. `mcp` is **read-only**: it has no photo-upload tool and no POI-creation tool (both removed — upload is REST-only, driven from `api`); don't assume symmetry with `api`'s endpoints.

**Observability** is wired through every service: `api`, `messaging`, and `mcp` export OTLP traces/metrics via OpenTelemetry, and `web` ships browser RUM (`@opentelemetry/sdk-trace-web`). Stage 1 runs the collector stack in Compose (`1-dockerize/observability/`); stages 3–4 deploy it as manifests (`*/manifests/observability/` — OTel Collector, Prometheus, Tempo, Grafana, postgres-exporter, cAdvisor). Stages 0 and 2 define no collector and set no `OTEL_*` vars, so exporters fall back to the default `localhost:4317` and export fails there — non-fatal, but expect connection-refused noise in those stages' logs.

## Working in this repo

- **Never refactor generated files**: `bin/`, `obj/`, `Migrations/` (per `AGENTS.md`).
- Stage directories 3 and 4 (`3-kubernetes/manifests/`, `4-argocd/manifests/`) look similar but drift from each other — check both if a fix belongs in "the k8s manifests" rather than assuming stage 4 is a strict superset of stage 3. Stage 3 numbers its manifest files (`2-deployment.yaml`) for `kubectl apply` ordering; stage 4 drops the numbers and uses Argo CD sync-wave annotations instead.
- **A service added to the app must be wired into all five stages**, not just the one you're working in: Compose (1, 2), the build helper (`2-dockerhub/docker-compose.build.yml`), manifests + ingress (3, 4), and every `runbook.ipynb`. `mcp` is the worked example of this — grep for `mcp` to see the full set of touch points. A *backing* service (one the app talks to rather than one we build) is wired into stage 0 as well, since stage 0 runs its infrastructure from Compose even though the app runs from source: `solr` is the worked example there — grep for `solr` / `SOLR_URL`.
- **Image tags are pinned per stage and must be bumped in lockstep.** They are currently aligned everywhere: `migrations:v1.0.8`, `api:v1.0.11`, `messaging:v1.0.14`, `mcp:v1.0.2`, `web:v1.0.7` (stages 2–4 pull `tshepontlhokoa/mytravels-*`; stage 1 builds the same tags locally). A bump touches six places: `2-dockerhub/docker-compose.build.yml` (authoritative — it's what gets built and pushed), `2-dockerhub/docker-compose.yml`, `1-dockerize/docker-compose.yml`, `0-local/docker-compose.yml` (it pulls `migrations` only), both manifest sets, and `src/scripts/merge-manifests.sh`. **Bumping the tag does not publish the image**: `docker-compose.build.yml` pushes per-arch tags (`-arm64` on macOS, `-amd64` elsewhere) and `merge-manifests.sh` merges them into the multi-arch tag the manifests actually reference — so a bump needs a build on *both* architectures before stages 2–4 will pull. Verify with `docker manifest inspect tshepontlhokoa/mytravels-<svc>:<tag>` before running a runbook past its image-pull step.
- **Web image runtime configuration** (`v1.0.7+`): the published `mytravels-web` image is built with sentinel placeholders (`__API_BASE_URL__`, `__OTLP_TRACES_ENDPOINT__`) for both Vite env vars, which are otherwise baked in at build time. Stages 3–4 substitute them with a `render-config` init container that copies the nginx docroot to an `emptyDir`, `sed`s in values from the `web-config` ConfigMap, and mounts that over the original docroot. Stage 2 does the same thing in Compose via a one-shot `render-web-config` service reading `.env` into a shared `web-docroot` volume. Stage 1 is the exception — it builds from source with literal URLs as build args and needs neither mechanism.
- **Image description/tagging feature** (async since `messaging:v1.0.11`): `AnthropicImageDescriptionService` (`src/common/mytravels.common/Services/`, implements `IImageDescriptionService`) calls Claude to generate a POI's `Description` and `Tags`, and **both are persisted** — `PointOfInterest.Description` plus rows via `spUpdatePointOfInterestTags`. It's invoked only from the `AppendImageTags` subscriber in `messaging` (published via the `append-image-tags` exchange above); the old synchronous REST describe endpoint and the web app's manual "Describe" button are gone, and `PoiDialog` renders `description`/`tags` straight from POI data. `AnthropicApiKey` therefore lives only on `messaging` (Compose env, and the `messaging` Secret in both manifest sets); the model is configurable alongside it via `AnthropicModel` (`messaging` ConfigMap / `ANTHROPIC_MODEL` in `.env`), defaulting in code to `claude-haiku-4-5`. Despite this, `api` and `mcp` still DI-register `IImageDescriptionService` because it's an unused constructor dependency of the shared `PointOfInterestService`; don't be misled into thinking `api` uses it. (The stale `AnthropicApiKey` placeholder that used to sit in `mytravels.api/appsettings.json` was removed with the SOLR work.)
- **Message traceability** (`api:v1.0.10` / `messaging:v1.0.13`): every POI gets a `CorrelationId` minted at creation and stamped onto all four published messages. `MessagePublisher` and `MessageSubscriberBase<T>` write `MessageAuditLogs` rows (`Published` / `ConsumeSucceeded` / `Retried` / `Failed`) through `IMessageAuditLogger`, **best-effort — an audit write failure is logged and never blocks publish or consume**. `MessageSubscriberBase<T>` also owns the retry policy for every subscriber: a failure republishes to the same exchange with an incremented `x-retry-count` header, and on the fourth attempt publishes a `FailedMessage` to the `<name>-failed` exchange and acks. Subscribers must therefore **rethrow** from `ProcessMessageAsync` for retry/dead-lettering to happen — swallowing an exception there silently marks the message consumed. Read side: `ITraceabilityService` → `GET /api/traceability` (paged correlation summaries) and `GET /api/traceability/{correlationId}` (event timeline), rendered by `src/web/src/components/TraceabilityPage.tsx` at the `/traceability` route. Adding a new exchange means adding its `-failed` twin to `ExchangeNames` *and* to `FailedExchangeDeclarer`.
- **POI search is SOLR-only** (`api:v1.0.11` / `mcp:v1.0.2` / `messaging:v1.0.14`). Both PostgreSQL search paths are gone: `CoreDbContext.SearchPointsOfInterestByFormattedAddressAsync` (the `ILIKE` on `FormattedAddress`) and `GetPointsOfInterestByTagAsync` were deleted along with the `spGetPointOfInterestByTagName` stored proc, which a drop migration removes from existing databases. `GET /api/pointofinterest/search` and `/filter`, and the `search_pointofinterest` MCP tool, all resolve through `ISolrSearchService` now; the `GetPointOfInterestResponse` row-per-tag shape is unchanged, so callers and both `ToDto` helpers are untouched. Things worth knowing before you change any of it:
  - **Documents are keyed on `PointOfInterestKey`, not `PointOfInterestId`.** Replacing a photo inserts a new row under the same key (`AddImageToPointOfInterestAsync` sets `Id = 0`), so keying on `Id` would strand the superseded row in the index forever and make a rebuild disagree with incremental indexing. Both paths resolve latest-row-per-key before indexing so they converge on the same document set. `poi_id` is stored as an ordinary field for DTO mapping.
  - **`tags`, `tag_exact` and `tag_ids` are written in the same order and must stay in sync.** Both `ToDto` helpers drop any tag whose `TagId` is null, so the search side has to pair a tag name back up with its PostgreSQL id — that's what `tag_ids` is for. `tag_exact` serves the `/filter` path, `tags` is the tokenised field that relevance ranks on.
  - **The schema lives in C#, not in a configset**: `SolrIndexService.EnsureSchemaAsync` PUTs field definitions idempotently and `SolrSchemaInitializer` (a hosted service in `messaging`) calls it at startup with backoff. One definition instead of a bind mount in Compose plus a ConfigMap in each Kubernetes stage. Adding a field means editing the `Fields` array in `SolrIndexService`, nothing else.
  - **`index-solr` is published three times per upload** — once at creation and once at the end of each of `AppendFormattedAddress` and `AppendImageTags`, because the address, description and tags all arrive asynchronously and are empty at creation time. `ResizeImage` does *not* index directly: it publishes `append-image-tags` instead, so the indexing write that follows it carries the description and tags rather than racing them. SOLR upserts by key, so repeat indexing is free. Each republish reuses the inbound `CorrelationId` so it lands on the same trace.
  - **Relevance boosts are query-time** (`qf=formatted_address^5 tags^3 description^1` in `SolrSearchService`) — retuning them needs no reindex and no schema change.
  - **Recovery is `POST /api/pointofinterest/reindex`**, which publishes to `reindex-solr` and returns `202` with a correlation id to follow through `/api/traceability`; the rebuild itself runs in `messaging` and never happens inline in the API.
  - **Behaviour change to be aware of**: `/filter` used to substring-match tag names *or* the address; it now filters on an exact tag name via `tag_exact`. Search also pages now — `rows` defaults to 100 (`SolrSearchQuery.Rows`), where the old `ILIKE` returned everything.
- **Uploading a photo with no GPS EXIF now throws.** `SaveFileAsPointOfInsterestAsync(file, ct)` raises `InvalidOperationException("Image is not geocoded")` rather than falling back to `(0,0)`; the no-GPS path is the separate `image/coordinates` endpoint where the client supplies a picked location. Older docs and any code assuming a `(0,0)` placeholder row is created on upload are out of date.
- Runbooks (`*/runbook.ipynb`) are validated with the `validate-runbook` skill (`.claude/skills/validate-runbook/`), which executes the notebook end-to-end and fixes failing cells. When editing a runbook cell by hand, follow its documented rule: on a failed health check, run the diagnostic command inline in the same cell rather than printing a suggestion to run one separately.
