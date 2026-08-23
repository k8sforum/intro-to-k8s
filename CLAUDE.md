# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Kubernetes learning course (KCNA/CKAD-oriented) built around one running example app, **MyTravels** (a .NET 10 / React geolocation Points-of-Interest app, source in `src/`). Five stages under the repo root deploy the *same* app with progressively more sophisticated tooling — each stage is a self-contained lesson:

| Stage | Directory | Adds |
|---|---|---|
| 0 | `0-local/` | Infra only (Postgres/RabbitMQ/MinIO) via Compose; app run from source |
| 1 | `1-dockerize/` | Full stack containerized, built locally via Compose; adds the observability stack |
| 2 | `2-dockerhub/` | Images built & pushed to Docker Hub, stack runs from registry images (no observability) |
| 3 | `3-kubernetes/` | Deployed to a k3d cluster via raw `kubectl apply` manifests + Traefik ingress |
| 4 | `4-argocd/` | Same manifests, GitOps-deployed via Argo CD (sync waves, drift/self-heal) |

The app deploys five units: `api` (5101), `messaging` (5102), `mcp` (5103), `web`, and `migration` (a one-shot job), on top of the shared libraries under `src/common/`.

Each stage directory has its own `.env`/`.env.example`, `docker-compose.yml` (0–3) or manifests (3–4), and a **`runbook.ipynb`** — the canonical hands-on walkthrough for that stage; treat it as the primary doc, not the `.md` files.

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
                       │
                       ▼ publish
                    RabbitMQ ──► messaging worker (5102) ──► MinIO (S3) / PostgreSQL
                       ▲
mcp (5103) ────────────┘  (MCP tool server — same domain services as api, no REST surface)
```

`.NET dependency graph`: `api`, `messaging`, and `mcp` all depend on `common`, `domain`, and `storage`, which all depend on `contract`; `migration` depends on `domain` only (design-time). `web` is a fully separate npm project with no dependency on the C# code — it talks to the API only over REST (`src/web/src/api/client.ts`).

Two RabbitMQ fanout exchanges drive async work: `resize-image` (thumbnail generation) and `append-formatted-address` (Google Maps/OSM geocoding, with a periodic sweeper retrying failures).

**`mytravels.mcp` (`src/api/mytravels.mcp/`)** is easy to miss — it sits under `src/api/` next to the REST API. It exposes three MCP tools (`upload_photo`, `upload_photo_with_coordinates`, `search_place`) over streamable HTTP via `MapMcp()`. It reuses `IPointOfInterestService`/`IMapsService` directly rather than calling the REST API, so **a change to POI creation behaviour affects both `api` and `mcp`** — check `src/api/mytravels.mcp/Tools/` whenever you touch those services.

**Observability** is wired through every service: `api`, `messaging`, and `mcp` export OTLP traces/metrics via OpenTelemetry, and `web` ships browser RUM (`@opentelemetry/sdk-trace-web`). Stage 1 runs the collector stack in Compose (`1-dockerize/observability/`); stages 3–4 deploy it as manifests (`*/manifests/observability/` — OTel Collector, Prometheus, Tempo, Grafana, postgres-exporter, cAdvisor). Stages 0 and 2 define no collector and set no `OTEL_*` vars, so exporters fall back to the default `localhost:4317` and export fails there — non-fatal, but expect connection-refused noise in those stages' logs.

## Working in this repo

- **Never refactor generated files**: `bin/`, `obj/`, `Migrations/` (per `AGENTS.md`).
- Stage directories 3 and 4 (`3-kubernetes/manifests/`, `4-argocd/manifests/`) look similar but drift from each other — check both if a fix belongs in "the k8s manifests" rather than assuming stage 4 is a strict superset of stage 3. Stage 3 numbers its manifest files (`2-deployment.yaml`) for `kubectl apply` ordering; stage 4 drops the numbers and uses Argo CD sync-wave annotations instead.
- **A service added to the app must be wired into all five stages**, not just the one you're working in: Compose (1, 2), the build helper (`2-dockerhub/docker-compose.build.yml`), manifests + ingress (3, 4), and every `runbook.ipynb`. `mcp` is the worked example of this — grep for `mcp` to see the full set of touch points.
- Image tags are pinned per stage and drift: the `web` Compose tag (`v1.0.4`) currently trails the build helper and k8s manifests (`v1.0.6`). Check the tag you're editing against `2-dockerhub/docker-compose.build.yml`, which is what actually gets built and pushed.
- Runbooks (`*/runbook.ipynb`) are validated with the `validate-runbook` skill (`.claude/skills/validate-runbook/`), which executes the notebook end-to-end and fixes failing cells. When editing a runbook cell by hand, follow its documented rule: on a failed health check, run the diagnostic command inline in the same cell rather than printing a suggestion to run one separately.
