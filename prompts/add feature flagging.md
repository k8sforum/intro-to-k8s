# Add OpenFeature-based feature flagging (Flagsmith)

**Status:** Approved for planning
**Date:** 2026-09-14

Add a self-hosted feature-flag backend with an admin console, fronted by the
OpenFeature spec on both sides of the app: the .NET services via the
OpenFeature .NET SDK, and the React web app via the OpenFeature Web/React SDK.
Demonstrate it with three flags, each shared across a .NET service and the
web app, rather than a no-op integration or a flag confined to one language.
Create and seed all three automatically as part of bringing the stack up via
the runbooks — no manual admin-console step.

## Problem

Nothing in this repo can toggle behaviour without a redeploy. Two places would
clearly benefit today:

- `AppendImageTags` (`src/messaging/mytravels.messaging/`) always calls
  `IImageDescriptionService` (Anthropic), which is a paid, external, and
  occasionally slow call. There is no way to turn it off in an environment
  without unsetting `AnthropicApiKey` and breaking the subscriber outright.
- `MapSearchBox` (`src/web/src/components/`), added in
  `prompts/add a search box.md`, is a good example of a feature you would want
  to roll out gradually or kill switch — there is currently no way to hide it
  without a code change and a rebuild.
- The `/traceability` page (`src/web/src/components/TraceabilityPage.tsx`)
  and its two entry points — the header's Map/Traceability toggle link and
  each POI's traceability link in `PoiDialog.tsx` — expose internal
  message-plumbing (correlation IDs, per-exchange retry/failure history)
  that isn't always wanted visible in every environment, and there is
  currently no way to hide it without a code change and a rebuild, same as
  `MapSearchBox`.

All three become OpenFeature flags evaluated through a real provider, not a
hard-coded config toggle.

## Current state (verified findings)

- **No feature-flag library exists anywhere in `src/`.** Grep for `flag` in
  `src/` only turns up `x-retry-count` and RabbitMQ `Flags`/tag fields.
  Nothing to migrate off, this is additive.
- **`AnthropicApiKey` lives only on `messaging`** (Compose env, and the
  `messaging` Secret in both manifest sets) — `api` and `mcp` DI-register
  `IImageDescriptionService` only because it is an unused constructor
  dependency of the shared `PointOfInterestService` (`CLAUDE.md`, "Image
  description/tagging feature"). The precedent still applies per-service:
  wire Flagsmith only into a .NET service that actually evaluates a flag.
  That now means both `messaging` (`enable-image-description`) and `api`
  (`enable-poi-search`, gating the `/search` endpoint) — not `mcp`, which
  has no flag of its own to evaluate.
- **Every backing service (Postgres/RabbitMQ/MinIO/SOLR) owns its own
  container, PVC, and health check** rather than sharing the app's Postgres —
  see `1-dockerize/docker-compose.yml` service blocks for `minio`, `rabbitmq`,
  `solr`, `postgres`, each independent. `src/scripts/init-dbs.sql`, referenced
  as a volume mount in every stage's `postgres` service, does not exist in the
  repo — a pre-existing stale reference, not a mechanism to build on.
- **Stage 3 manifests are numbered per backing service**
  (`3-kubernetes/manifests/solr/{1-pvc,2-deployment,3-service}.yaml`, same
  shape for `minio`, `rabbitmq`, `postgres`); stage 4 mirrors the same three
  files without numbers plus sync-wave annotations
  (`4-argocd/manifests/solr/{deployment,pvc,service}.yaml`). Any hostPath PV in
  this cluster is node-pinned — `minio/2-pv-pvc.yaml`'s `nodeAffinity` to
  `k3d-mytravels-agent-2`, which `solr/1-pvc.yaml` matches.
- **Web runtime config is sentinel-substituted, not baked in.** Vite env vars
  are compiled into the bundle at build time, so the published image is built
  with placeholders (`__API_BASE_URL__`, `__OTLP_TRACES_ENDPOINT__`,
  `2-dockerhub/docker-compose.build.yml`) and substituted at pod start by a
  `render-config` init container in stages 3–4
  (`3-kubernetes/manifests/web/1-configmap.yaml`) and a one-shot Compose
  service in stage 2. Stage 1 builds from source with literal build args and
  needs neither mechanism (`CLAUDE.md`, "Web image runtime configuration").
  Any new build-time web config value must follow this same three-sentinel
  pattern, not a fourth bespoke mechanism.
- **Currently pinned tags** (`1-dockerize/docker-compose.yml`):
  `messaging:v1.0.15`, `web:v1.0.8`. Bump from whatever is currently pinned at
  implementation time, not from a hardcoded number here — `CLAUDE.md` already
  notes these drift.
- **Ingress host list** (`3-kubernetes/manifests/9-ingress.yaml`) is a single
  `Ingress` resource with one `host` rule block per backing service's admin
  UI, plus a comment block at the top enumerating every exposed host. `solr`
  is the most recent addition to both.
- **Traceability has two UI entry points and two read endpoints, nothing
  else.** `App.tsx`'s `Header` renders a single Map/Traceability toggle
  `Link` (always visible, regardless of route) and `PoiDialog.tsx:154` links
  to `/traceability?correlationId=...` for a given POI; those are the only
  two places that link into the page, and `App.tsx:39` is the only route
  registration. The reads themselves are `GET /api/Traceability` and `GET
  /api/Traceability/{correlationId}` in `TraceabilityController.cs`
  (`src/api/mytravels.api/Controllers/`) — `api` is already the service that
  will evaluate `enable-poi-search`, so the third flag needs no new .NET
  service wired, just a second flag evaluated on an already-wired one.
- **The React app has no state/provider wrapper yet** —
  `src/web/src/main.tsx` renders `<BrowserRouter><App /></BrowserRouter>`
  directly, after importing `./telemetry` for side effects. A new top-level
  provider is a small, additive change here.

## Design decisions

- **Backend: self-hosted Flagsmith**, not flagd. flagd has no admin UI at all
  (flags are files or CRDs); Unleash's OpenFeature coverage has no official
  .NET provider. Flagsmith is the only option with both a real admin console
  *and* first-party OpenFeature providers for .NET and React/web, per the
  research this prompt follows from.
- **Flagsmith shares `mytravels-postgres`, in a second database, not a
  second schema and not a second container.** The app's own data stays
  exactly where it is — `CoreDbContext` continues to point at the `CoreDb`
  database (`CORE_DB_CONTEXT` in `.env.example`), untouched. Flagsmith gets
  its own sibling database, `FeatureDb`, on the same running Postgres
  instance. Database-level separation is the right unit here: Postgres
  isolates catalogs, roles, and migration-history tables per database (no
  risk of Flagsmith's Django migrations ever colliding with EF Core's
  `EFMigrationsHistory`), so a schema-level split would be strictly weaker
  for no benefit, while a second Postgres *container* would be strictly
  heavier for no benefit — this cluster has no HA or independent-scaling
  requirement that would justify the extra pod, PVC, and node-affinity
  pinning a dedicated instance would need. This is a deliberate, narrow
  exception to "every backing service owns its own container" (Current state
  above) — that pattern is about services with genuinely different failure
  domains (RabbitMQ, MinIO, SOLR); a second database on an already-running
  Postgres is not that.
- **The `flagsmith` database is created by a small idempotent step, not by
  `docker-entrypoint-initdb.d`.** That mechanism only runs against a
  completely fresh volume on first boot, so it would silently do nothing for
  everyone who already has a `pgdata`/`postgres-data-pvc` volume from before
  this change. Instead, add a one-shot `flagsmith-create-db` step that
  connects to the existing `postgres` service as `POSTGRES_USER` and runs the
  same idempotent guard the existing `cleanup-migrations` Compose service
  already uses for DDL:
  `psql -tc "SELECT 1 FROM pg_database WHERE datname = 'FeatureDb'" | grep -q 1 || psql -c 'CREATE DATABASE "FeatureDb"'`
  — the identifier is quoted so Postgres doesn't fold it to lowercase, the
  same way the existing `CoreDb` name is preserved.
  In Compose this is a fifth service depending on `postgres: service_healthy`;
  in Kubernetes (stages 3–4) it is a `Job` using the same `postgres-secret`,
  run once before the Flagsmith `migrate` Job. Flagsmith connects with the
  same `POSTGRES_USER`/`POSTGRES_PASSWORD` the app already uses — this repo
  has no per-service database roles today (`SPEC.md` §11: unauthenticated
  throughout), so a second superuser-ish credential would be inconsistent,
  not more secure.
- **Because Flagsmith no longer owns any storage of its own, it needs no PVC
  and no node affinity.** All of its state lives in the shared Postgres
  volume, which already exists and is already pinned. This removes what
  would otherwise have been the trickiest part of the Kubernetes stages.
- **Remaining topology follows Flagsmith's own `docker-compose.yml` (minus
  its Postgres)**: a one-shot `flagsmith-migrate` service (command
  `migrate`), a `flagsmith` service (command `serve`, port 8000, serves both
  the REST API and the admin console UI from one image), and a
  `flagsmith-task-processor` service (command `run-task-processor`, same
  image, no ports needed on the host). No separate frontend container — the
  plain Docker image bundles UI and API together, unlike the Helm chart's
  split components. Pin an explicit tag (verify the current stable tag on
  `docker.flagsmith.com/flagsmith/flagsmith` before writing it down — do not
  use `latest`, everything else in this repo pins).
- **`messaging` and `api` are wired to Flagsmith; `mcp` is not.**
  `AppendImageTags` (`messaging`) evaluates `enable-image-description`; `api`
  evaluates both `enable-poi-search` (`/search`) and `enable-message-tracing`
  (`TraceabilityController`'s two read actions) — a second flag on an
  already-wired service, not a second service. All three flags are shared
  between a .NET service and `web` — see below — so each side reads the
  same flag key rather than a language-scoped one. `mcp` still gets nothing,
  since it evaluates none of the three: wiring it in would reintroduce the
  exact "unused constructor dependency" confusion `CLAUDE.md` already calls
  out for `IImageDescriptionService`.
- **Three flags, each shared across a .NET service and `web`, all boolean,
  all created and seeded automatically** — see the seeding design decision
  below — rather than by hand in the admin console. Each flag key is
  evaluated from both a server-side environment key (`messaging` or `api`)
  and the client-side environment key (`web`) — Flagsmith issues one key per
  side per environment, not one per flag, so sharing a flag across languages
  needs no extra Flagsmith-side setup beyond creating the flag once:

  | Flag key | Type | Default | Evaluated by | Gates |
  |---|---|---|---|---|
  | `enable-image-description` | boolean | `true` | `messaging` (.NET, server-side key) **and** `web` (React, client-side key) | Backend: the Anthropic call and the `Description`/tags persistence in `AppendImageTags`. Frontend: whether `PoiDialog` renders the description/tags section at all |
  | `enable-poi-search` | boolean | `true` | `api` (.NET, server-side key) **and** `web` (React, client-side key) | Backend: `GET /api/pointofinterest/search` returns `404` when off. Frontend: whether `MapSearchBox` renders at all in `MapPage` |
  | `enable-message-tracing` | boolean | `true` | `api` (.NET, server-side key) **and** `web` (React, client-side key) | Backend: `GET /api/Traceability` and `GET /api/Traceability/{correlationId}` return `404` when off. Frontend: whether the header's Traceability link (`App.tsx`) and each POI's traceability link (`PoiDialog.tsx`) render at all |

- **`AppendImageTags` still publishes `index-solr` when the flag is off.** It
  just skips the Anthropic call and the `Description`/tag persistence before
  doing so — the POI still gets indexed (without description/tags), matching
  how `index-solr` already tolerates partial data at every other stage of the
  pipeline. Do not skip the message entirely; that would leave the POI
  unindexed for this pass.
- **`enable-message-tracing` gates reads only — `MessageAuditLogger` keeps
  writing `MessageAuditLogs` rows regardless of the flag.** Turning the flag
  off hides the traceability UI and 404s the read endpoints; it does not
  stop `messaging`/`api` from recording `Published`/`ConsumeSucceeded`/
  `Retried`/`Failed` events. This matches the existing best-effort,
  never-blocks-the-pipeline treatment of audit writes (`CLAUDE.md`, "Message
  traceability") and means turning the flag back on immediately shows a
  complete history rather than a gap.
- **The two evaluations of a shared flag key can disagree for a few seconds
  after a toggle**, since `messaging`/`api` and `web` each hold their own
  provider subscription/polling loop and refresh independently. That's an
  accepted, temporary inconsistency, not something to build synchronization
  for.
- **Flags are created and seeded automatically, not by hand — but bootstrap
  yields a password-reset link, not a usable password.** Self-hosted
  Flagsmith's `bootstrap` management command (enabled via
  `ALLOW_ADMIN_INITIATION_VIA_CLI=true`, driven by `ADMIN_EMAIL` /
  `ORGANISATION_NAME` / `PROJECT_NAME` env vars — confirm exact variable
  names and behaviour against the pinned image's docs before writing the
  seed step; this prompt was written against Flagsmith's published docs, not
  a compiled/run instance) creates the initial superuser, organisation, and
  project on first boot, and logs a "set your password" link rather than
  handing back a password. A new one-shot `flagsmith-seed` step therefore
  has to: (1) find that link in `docker compose logs flagsmith` (`kubectl
  logs`, Stage 3–4) and parse the reset token out of it, (2) use the token
  to set a known password via the same endpoint the reset-password page
  itself calls, (3) log in and obtain a session/token, (4) idempotently
  create (upsert-by-name, safe to rerun) an environment under the bootstrap
  project and the three flags from the table above at their default `true`,
  and (5) read back that environment's server-side and client-side keys.
  Every one of these five steps needs confirming against a running instance
  before the script is written — this prompt establishes the shape, not the
  exact endpoints or log format. A small `curl`-plus-`jq` script in a
  generic image (matching how `flagsmith-create-db` already uses
  `postgres:17.6-alpine` rather than a custom build) is the preferred
  implementation vehicle over the Flagsmith CLI binary, since it avoids
  adding a fourth image this repo would need to build/tag/push across both
  architectures — fall back to the CLI only if curl alone can't script the
  auth flow.
- **Seeding needs the stack brought up in two phases, the same shape as the
  manual flow it replaces.** Neither Compose nor Kubernetes lets a sibling's
  already-materialized environment be edited after the fact — Compose
  interpolates `${FLAGSMITH_SERVER_SIDE_ENVIRONMENT_KEY}` /
  `${VITE_FLAGSMITH_ENVIRONMENT_ID}` from `.env` once, at `docker compose
  up` parse time, before `flagsmith-seed` has run; a Kubernetes Secret has
  to exist before a Deployment that mounts it starts cleanly. So the runbook
  brings the stack up in two scoped steps — `postgres` + the Flagsmith
  services (+ `flagsmith-seed`) first, then `messaging`/`api`/`web` once
  `.env` (Compose) or the generated `flagsmith-keys` Secret (Kubernetes) is
  in place — mirroring the previous "stand up Flagsmith, do the manual step,
  then start the rest" shape, just with the middle step scripted instead of
  done by hand.
- **Web gets a fourth sentinel**, `__FLAGSMITH_ENVIRONMENT_ID__` /
  `VITE_FLAGSMITH_ENVIRONMENT_ID`, following the existing two exactly through
  every place they appear (Dockerfile build arg, `docker-compose.build.yml`
  sentinel default, stage 2's one-shot render service, stage 3/4
  `web/1-configmap.yaml` + `render-config` init container, stage 1's literal
  build arg, stage 0's plain `.env` var since the web app isn't containerized
  there).
- **No new RabbitMQ exchange, no new database table.** Flag evaluation is a
  synchronous read inside the existing subscriber/component; it does not need
  message-based propagation or its own persistence beyond what Flagsmith
  brings.

## Changes

### Messaging (`src/messaging/mytravels.messaging`)

1. Add NuGet package `OpenFeature.Contrib.Providers.Flagsmith` (brings in
   `OpenFeature` core transitively) — verify the current published version
   before pinning.
2. `appsettings.json` — add a `Flagsmith` section: `ApiUri`,
   `ServerSideEnvironmentKey`.
3. `Program.cs` — construct and register the provider before `app.Build()`:
   ```csharp
   var flagsmithProvider = new FlagsmithProvider(
       new FlagsmithProviderConfiguration(),
       new FlagsmithConfiguration
       {
           ApiUri = new Uri(builder.Configuration["Flagsmith:ApiUri"]!),
           EnvironmentKey = builder.Configuration["Flagsmith:ServerSideEnvironmentKey"],
           EnableAnalytics = false,
           Retries = 1,
       });
   await OpenFeature.Api.Instance.SetProviderAsync(flagsmithProvider);
   builder.Services.AddSingleton(OpenFeature.Api.Instance.GetClient("mytravels-messaging"));
   ```
   Confirm the exact constructor/config member names against the installed
   package version's README — this prompt was written against the NuGet
   listing, not a compiled sample.
4. `AppendImageTags.cs` — inject the registered `FeatureClient`. Before
   calling `IImageDescriptionService`, evaluate
   `await featureClient.GetBooleanValueAsync("enable-image-description", true)`.
   When `false`, skip the Anthropic call and the `Description`/tag writes,
   then continue to the existing tail-end `index-solr` publish unchanged (see
   design decision above).

### API (`src/api/mytravels.api`)

1. Add NuGet package `OpenFeature.Contrib.Providers.Flagsmith` — same package
   and version as `messaging` (verify current version once, pin identically
   in both `.csproj` files).
2. `appsettings.json` — same `Flagsmith` section as `messaging`: `ApiUri`,
   `ServerSideEnvironmentKey`. Same environment key value as `messaging` —
   Flagsmith's server-side key is per-environment, not per-service, so both
   services authenticate with the same key.
3. `Program.cs` — same provider construction and registration pattern as
   `messaging` (see above), with `GetClient("mytravels-api")` as the client
   name.
4. The `/search` endpoint's controller action (`PointOfInterestController`,
   wherever it maps `GET /api/pointofinterest/search`) — inject the
   registered `FeatureClient`. Evaluate
   `await featureClient.GetBooleanValueAsync("enable-poi-search", true)` at
   the top of the action; when `false`, return `NotFound()` before touching
   `IPointOfInterestService`. The plain list endpoint (`GET
   /api/pointofinterest`) is untouched — only `/search` is gated.
5. `TraceabilityController.cs` — inject the same registered `FeatureClient`.
   At the top of both actions (`GET /api/Traceability`, `GET
   /api/Traceability/{correlationId}`), evaluate `await
   featureClient.GetBooleanValueAsync("enable-message-tracing", true)`; when
   `false`, return `NotFound()` before calling `ITraceabilityService`.
   `MessageAuditLogger`'s writes are untouched (see design decision above) —
   this flag gates the two read actions only.

### Web (`src/web`)

1. Add npm packages `@openfeature/web-sdk`, `@openfeature/react-sdk`,
   `@openfeature/flagsmith-client-provider` — verify current versions on npm
   before pinning.
2. `src/web/src/main.tsx` — before rendering, set the provider and wrap `App`:
   ```tsx
   import { OpenFeature } from '@openfeature/web-sdk'
   import { OpenFeatureProvider } from '@openfeature/react-sdk'
   import { FlagsmithClientProvider } from '@openfeature/flagsmith-client-provider'

   await OpenFeature.setProviderAndWait(
     new FlagsmithClientProvider({ environmentID: import.meta.env.VITE_FLAGSMITH_ENVIRONMENT_ID }),
   )
   ```
   then render `<OpenFeatureProvider><BrowserRouter><App /></BrowserRouter></OpenFeatureProvider>`.
3. `MapPage.tsx` — gate `MapSearchBox`'s render behind the
   `enable-poi-search` boolean flag using `@openfeature/react-sdk`'s flag hook.
   Confirm the exact hook name/signature (`useFlag` vs
   `useBooleanFlagValue`/similar) against the installed SDK version's README
   before writing the call — do not guess the API surface.
4. `PoiDialog.tsx` (or wherever it lives under `src/web/src/components/`) —
   gate the description/tags section behind `enable-image-description` using
   the same flag hook. When off, render the dialog without that section
   rather than showing empty/placeholder text — the flag should hide it even
   for POIs saved before it was toggled off, not just suppress future writes.
5. `App.tsx`'s `Header` — gate the Map/Traceability toggle `Link` behind
   `enable-message-tracing` using the same flag hook; when off, render just
   the logo/title, no link. `PoiDialog.tsx:154`'s
   `to="/traceability?correlationId=..."` link — gate the same way; when
   off, show the correlation id as plain text, not a link. Direct
   navigation to `/traceability` while the flag is off still works
   client-side (the route stays registered) but the page's own fetches will
   404 against the now-gated API — matching how `enable-poi-search` already
   leaves `/search` gated independent of whether `MapSearchBox` is visible.
6. `.env.example` (or equivalent Vite env doc) — document
   `VITE_FLAGSMITH_ENVIRONMENT_ID`.

### Infrastructure — all five stages

Backing-service rule applies (`CLAUDE.md`): Flagsmith is infrastructure we run
but don't build, so it belongs in stage 0's Compose file too, same as SOLR.

**Stage 0 / 1 / 2 (Compose)** — add to each `docker-compose.yml`, alongside
the existing `postgres` service (do not touch its `POSTGRES_DB`/`CoreDb`
setup):
```yaml
flagsmith-create-db:
  image: postgres:17.6-alpine
  container_name: flagsmith-create-db
  depends_on:
    postgres: { condition: service_healthy }
  environment:
    POSTGRES_USER: ${POSTGRES_USER}
    POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
  restart: "no"
  entrypoint: ["sh", "-c"]
  command: |
    PGPASSWORD="$$POSTGRES_PASSWORD" psql -h postgres -U "$$POSTGRES_USER" -tc "SELECT 1 FROM pg_database WHERE datname = 'FeatureDb'" | grep -q 1 || \
    PGPASSWORD="$$POSTGRES_PASSWORD" psql -h postgres -U "$$POSTGRES_USER" -c 'CREATE DATABASE "FeatureDb"'

flagsmith-migrate:
  image: docker.flagsmith.com/flagsmith/flagsmith:<verify-tag>
  command: migrate
  environment: &flagsmith-env
    DATABASE_URL: postgresql://${POSTGRES_USER}:${POSTGRES_PASSWORD}@postgres:5432/FeatureDb
    DJANGO_SECRET_KEY: ${FLAGSMITH_DJANGO_SECRET_KEY}
    ENVIRONMENT: production
  depends_on:
    flagsmith-create-db: { condition: service_completed_successfully }

flagsmith:
  image: docker.flagsmith.com/flagsmith/flagsmith:<verify-tag>
  command: serve
  ports: ["8000:8000"]
  environment: *flagsmith-env
  depends_on:
    flagsmith-migrate: { condition: service_completed_successfully }
  healthcheck:
    test: ["CMD-SHELL", "curl -f http://localhost:8000/health || exit 1"]  # verify the real path against a running instance

flagsmith-task-processor:
  image: docker.flagsmith.com/flagsmith/flagsmith:<verify-tag>
  command: run-task-processor
  environment: *flagsmith-env
  depends_on:
    flagsmith-migrate: { condition: service_completed_successfully }

flagsmith-seed:
  image: curlimages/curl:<verify-tag>  # or a purpose-built image if jq/scripting needs outgrow curl alone
  container_name: flagsmith-seed
  depends_on:
    flagsmith: { condition: service_healthy }
  environment:
    FLAGSMITH_API_URL: http://flagsmith:8000/api/v1
    ADMIN_EMAIL: ${FLAGSMITH_ADMIN_EMAIL}
  restart: "no"
  volumes:
    - ./.env:/workspace/.env   # script rewrites the two generated key lines in place
  entrypoint: ["sh", "-c", "<seed script — see design decision above; parses the password-reset link from `docker compose logs flagsmith`, sets a password, logs in, upserts the environment + three flags, writes the two keys into /workspace/.env>"]
```
No new named volume — everything lands in the existing `pgdata` volume.
`messaging` and `api` (and `web` in stages 1–2) gain
`Flagsmith__ApiUri`/`Flagsmith__ServerSideEnvironmentKey` env vars and a
`depends_on: flagsmith: { condition: service_healthy }`, same as before —
but `depends_on` alone doesn't solve key propagation: Compose interpolates
`${FLAGSMITH_SERVER_SIDE_ENVIRONMENT_KEY}`/`${VITE_FLAGSMITH_ENVIRONMENT_ID}`
from `.env` once, at `docker compose up` parse time, before `flagsmith-seed`
has run. The runbook cell therefore brings the stack up in two scoped
invocations — `postgres` + the Flagsmith services (+ `flagsmith-seed`)
first, then the rest once `.env` has been rewritten — see design decision
above. `.env.example` for every Compose stage: `FLAGSMITH_DJANGO_SECRET_KEY`
and `FLAGSMITH_ADMIN_EMAIL` as real values; `FLAGSMITH_SERVER_SIDE_ENVIRONMENT_KEY`
and `VITE_FLAGSMITH_ENVIRONMENT_ID` as empty placeholders with a comment
noting `flagsmith-seed` fills them in — they're seeded, not entered by hand.
No new Postgres credentials, Flagsmith reuses
`POSTGRES_USER`/`POSTGRES_PASSWORD`.

**Stage 3** (`3-kubernetes/manifests/flagsmith/`, numbered like `solr/`, but
no PVC — see design decision above): `1-secret.yaml` (Django secret only —
DB credentials come from the existing `postgres-secret`, and the Flagsmith
environment keys are no longer hand-entered here, see the seed Job below),
`2-create-db-job.yaml` (a `Job` running the same idempotent `psql` guard as
the Compose step, using `postgres-secret`), `3-migrate-job.yaml` (a `Job`
running `migrate`, ordered after the create-db Job completes),
`4-deployment.yaml` (the `flagsmith` `serve` container),
`4b-task-processor-deployment.yaml`, `4c-service.yaml`,
`5-seed-rbac.yaml` (a `ServiceAccount` plus a `Role`/`RoleBinding` scoped to
`create`/`update` on `secrets` in this namespace only — the seed Job needs
to write the keys it generates somewhere the other Deployments can read),
and `6-seed-job.yaml` (a `Job` running the same seeding logic as
`flagsmith-seed` in Compose, but writing
`FLAGSMITH_SERVER_SIDE_ENVIRONMENT_KEY`/`VITE_FLAGSMITH_ENVIRONMENT_ID` into
a new `flagsmith-keys` Secret via the Kubernetes API instead of a `.env`
file). `messaging` and `api`'s Deployments gain a `secretKeyRef` to
`flagsmith-keys` for `Flagsmith__ServerSideEnvironmentKey`; `web`'s
`render-config` init container (`web/1-configmap.yaml`) gains the same
Secret as an additional env source for the `VITE_FLAGSMITH_ENVIRONMENT_ID`
sentinel. `kubectl apply`'s file-order-only guarantee means the seed Job's
manifests come before those three Deployments' numerically, but applying
doesn't block on a Job finishing — the runbook cell needs an explicit
`kubectl wait --for=condition=complete job/flagsmith-seed` between applying
the two groups, mirroring Compose's two-phase bring-up. Add
`flagsmith.mytravels.local` → `flagsmith:8000` to `9-ingress.yaml`'s rule
list and its host comment block.

**Stage 4** (`4-argocd/manifests/flagsmith/`): same files without number
prefixes, `sync-wave` annotations placing the create-db and migrate Jobs in
the same wave as Postgres (they need it healthy, and everything else needs
them done first), then `flagsmith`/`flagsmith-task-processor` one wave
later, then the seed `ServiceAccount`/`Role`/`RoleBinding`/`Job` one wave
after that, ahead of `messaging`/`api`/`web`. Argo CD's sync-wave ordering
already waits for each wave to report healthy/complete before starting the
next — including `Job` completion — so this stage doesn't need Stage 3's
manual `kubectl wait`.

**Image tags**: `messaging`, `api`, and `web` all change (new dependency,
new runtime config) — bump in all six of the usual places
(`2-dockerhub/docker-compose.build.yml`, `2-dockerhub/docker-compose.yml`,
`1-dockerize/docker-compose.yml`, `0-local/docker-compose.yml` if it pulls
those images, both manifest sets, `src/scripts/merge-manifests.sh`). `api` is
already unpublished (`CLAUDE.md`: `api:v1.0.12` not published yet), so this
adds to rather than creates that pre-existing gap. Do not build or push; flag
in the summary that stages 2–4 will not pull until both architectures are
built and merged for all three images.

### Documentation

- `CLAUDE.md` — add Flagsmith to the architecture sketch and to the
  five-stage-wiring checklist (it is a backing service, so stage 0 included,
  per the existing SOLR-is-the-worked-example note). Note the three flags,
  that each is evaluated by both a .NET service (`messaging` or `api`) and
  `web`, and that all three are seeded automatically rather than created by
  hand.
- `SPEC.md` — add Flagsmith as an integration alongside SOLR/MinIO/RabbitMQ,
  document the `Flagsmith` config section, and add `enable-image-description`
  to whatever section currently describes `AppendImageTags`'s behaviour, plus
  `enable-poi-search`/`enable-message-tracing` to the sections describing
  `/search` and `/api/Traceability` respectively.
- All five `runbook.ipynb` — a Flagsmith section per stage, scripted end to
  end, no manual admin-console step: bring up `postgres` + the Flagsmith
  services scoped (e.g. `docker compose up -d postgres flagsmith-create-db
  flagsmith-migrate flagsmith flagsmith-task-processor`, or the Stage 3/4
  equivalent `kubectl apply` subset), confirm `flagsmith`/
  `flagsmith-task-processor` are healthy, run the seeding step
  (`flagsmith-seed`, or `kubectl apply -f flagsmith/6-seed-job.yaml` +
  `kubectl wait --for=condition=complete`), confirm the three flags exist
  via a `curl` against the Flagsmith API using the now-written keys, *then*
  bring up the remaining services. Unlike `GOOGLE_API_KEY`/`ANTHROPIC_API_KEY`
  — third-party credentials with no self-hosted bootstrap path, and still a
  manual runbook step — Flagsmith's own bootstrap command means this one
  needs no manual step at all. Per the runbook rule, any *automatable*
  health check that fails must still run its diagnostic inline in the same
  cell, never print a suggestion.
- `drawio/architecture.drawio` — add Flagsmith and its Postgres, and three new
  dotted "evaluates a flag" edges: `messaging` → Flagsmith, `api` →
  Flagsmith, and `web` → Flagsmith.

## Implementation order

1. Stand up the Flagsmith Compose stack in `1-dockerize` (Postgres + Flagsmith
   services only). Build and verify `flagsmith-seed` end to end against it —
   bootstrap, password-reset-link parsing, login, upsert environment + all
   three flags, key retrieval, `.env` rewrite — before writing any
   application code. This is the riskiest, least-verified part of this
   prompt; expect to iterate here.
2. `messaging`: NuGet package, config section, provider registration,
   `AppendImageTags` gating on `enable-image-description`. Verify both on
   and off — confirm `Description`/tags are skipped and `index-solr` still
   fires when off.
3. `api`: same NuGet package and provider registration pattern, `/search`
   gating on `enable-poi-search` and `TraceabilityController` gating on
   `enable-message-tracing`. Verify each flag independently, both on and
   off — confirm the respective endpoints 404 when off.
4. `web`: npm packages, provider registration in `main.tsx`, gating in
   `MapPage.tsx`, `PoiDialog.tsx` (both the description/tags section and the
   traceability link), and `App.tsx`'s `Header`. Verify in the browser with
   each of the three flags both on and off, including that each flag's
   backend and frontend halves move together.
5. Replicate the Compose service blocks (including `flagsmith-seed`) and the
   two-phase bring-up into stages 0 and 2's runbooks. Add the fourth web
   sentinel end to end (Dockerfile, build.yml, stage 2's render service).
6. Stage 3 manifests (create-db Job, migrate Job, deployments, service, seed
   RBAC + Job), ingress entry. Deploy to k3d, repeat the on/off verification
   for all three flags through the cluster, including the `kubectl wait`
   two-phase apply.
7. Stage 4 manifests with sync-wave annotations, including the seed Job's
   wave ordering ahead of `messaging`/`api`/`web`.
8. Bump and push image tags for `messaging`, `api`, and `web`.
9. Documentation, runbooks (all five stages), diagram.

## Decisions already made

| Question | Decision |
|---|---|
| Backend | Self-hosted Flagsmith, not flagd/Unleash |
| Flagsmith's database | A second database (`FeatureDb`) on the existing `mytravels-postgres` instance — not a schema, not a second container. The app's own data stays in `CoreDb`, untouched |
| Database creation | An idempotent create-db step (Compose service / k8s Job), not `docker-entrypoint-initdb.d` — the latter only fires on a fresh volume |
| Which .NET services get wired | `messaging` and `api` — `api` evaluates two flags (`enable-poi-search`, `enable-message-tracing`), `messaging` one, `mcp` none |
| What happens to `index-solr` when the flag is off | Still published, just without description/tags |
| What happens to audit-log writes when `enable-message-tracing` is off | Still written — only the read endpoints and UI are gated |
| Web config delivery | Fourth sentinel, same substitution mechanism as the existing two |
| Flag creation | Automated: Flagsmith's own `bootstrap` command plus a scripted `flagsmith-seed` step create the org/project/environment and all three flags — no manual admin-console step |
| Image tags | Bump `messaging`, `api`, and `web` everywhere; do not build or push |

## Out of scope

- Flagsmith's segmentation, A/B testing, remote config (non-boolean) features,
  and its analytics/usage dashboard.
- Any RBAC/SSO on the Flagsmith admin console — it is unauthenticated-by-default
  like every other admin UI in this stack (`SPEC.md` §11), gated only by
  whoever signs up first.
- The Flagsmith Helm chart. Stages 3–4 use raw manifests for every other
  backing service; Flagsmith gets the same treatment rather than introducing
  Helm into a repo that otherwise has none.
- Wiring `mcp` to Flagsmith. Revisit only if a flag it actually evaluates is
  identified later — `search_pointofinterest` is a plausible candidate but
  isn't part of this prompt.
- A dedicated Postgres role/credential for Flagsmith, separate from
  `POSTGRES_USER`. Revisit only if this repo ever introduces per-service
  database credentials generally — doing it for Flagsmith alone would be
  inconsistent with every other service.
- Automating `FLAGSMITH_DJANGO_SECRET_KEY` generation. It stays a value the
  runbook documents as something to set once, the same category as every
  other example secret in this repo (`SPEC.md` §11: unauthenticated
  throughout) — only the *flags* are seeded, not Flagsmith's own app secret.
- A Flagsmith Management API key / PAT for the seed script. It authenticates
  as the bootstrap admin user directly rather than minting a longer-lived
  token, since generating a PAT is itself an admin-console step this prompt
  is trying to avoid.
- Retrying or self-healing the seed step beyond a single idempotent run plus
  a runbook rerun. No controller watches for drift between Flagsmith's state
  and the three flags this prompt expects to exist.
