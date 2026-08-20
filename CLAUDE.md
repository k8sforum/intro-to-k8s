# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Kubernetes learning course (KCNA/CKAD-oriented) built around one running example app, **MyTravels** (a .NET 10 / React geolocation Points-of-Interest app, source in `src/`). Five stages under the repo root deploy the *same* app with progressively more sophisticated tooling — each stage is a self-contained lesson:

| Stage | Directory | Adds |
|---|---|---|
| 0 | `0-local/` | Infra only (Postgres/RabbitMQ/MinIO) via Compose; app run from source |
| 1 | `1-dockerize/` | Full stack containerized, built locally via Compose |
| 2 | `2-dockerhub/` | Images built & pushed to Docker Hub, stack runs from registry images |
| 3 | `3-kubernetes/` | Deployed to a k3d cluster via raw `kubectl apply` manifests + Traefik ingress |
| 4 | `4-argocd/` | Same manifests, GitOps-deployed via Argo CD (sync waves, drift/self-heal) |

Each stage directory has its own `.env`/`.env.example`, `docker-compose.yml` (0–3) or manifests (3–4), and a **`runbook.ipynb`** — the canonical hands-on walkthrough for that stage; treat it as the primary doc, not the `.md` files.

**`SPEC.md`** at the repo root is a full reverse-engineered technical spec (architecture, APIs, data model, config surface, and a catalogued list of known bugs/dead code/fragile logic). Read it before making non-trivial changes to `src/` instead of re-deriving architecture from scratch.

## Commands

**.NET (`src/`, solution: `src/mytravels.sln`)**
```bash
dotnet build src/mytravels.sln
cd src/api/mytravels.api && dotnet run          # API, port 5101, Swagger at /swagger
cd src/messaging/mytravels.messaging && dotnet run  # background worker, port 5102
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
```

`.NET dependency graph`: `api` and `messaging` both depend on `common`, `domain`, and `storage`, which all depend on `contract`; `migration` depends on `domain` only (design-time). `web` is a fully separate npm project with no dependency on the C# code — it talks to the API only over REST (`src/web/src/api/client.ts`).

Two RabbitMQ fanout exchanges drive async work: `resize-image` (thumbnail generation) and `append-formatted-address` (Google Maps/OSM geocoding, with a periodic sweeper retrying failures).

## Working in this repo

- **Never refactor generated files**: `bin/`, `obj/`, `Migrations/` (per `AGENTS.md`).
- Stage directories 3 and 4 (`3-kubernetes/manifests/`, `4-argocd/manifests/`) look similar but drift from each other — check both if a fix belongs in "the k8s manifests" rather than assuming stage 4 is a strict superset of stage 3.
- Runbooks (`*/runbook.ipynb`) are validated with the `validate-runbook` skill (`.claude/skills/validate-runbook/`), which executes the notebook end-to-end and fixes failing cells. When editing a runbook cell by hand, follow its documented rule: on a failed health check, run the diagnostic command inline in the same cell rather than printing a suggestion to run one separately.
