# Lab - Registry-Sourced Paperless-ngx

Independent practice for this stage's concept: run a stack entirely from pinned
registry images, with configuration and secrets kept out of the compose file
itself. No custom images to build here (unlike MyTravels, paperless-ngx has no
`src/` of ours to build) - the lesson is version pinning and `.env`-driven
config, not building.

## What's different from the stage 1 lab

| | Stage 1 lab | Stage 2 lab |
|---|---|---|
| Services | 5 (adds gotenberg + tika) | 3 (db, broker, webserver only) |
| Image tags | loose (`latest`, `alpine`) | pinned exact versions |
| Config | inline `environment:` | `.env` file via `env_file:` |

## Your task

1. Copy `starter-project/.env.example` to `starter-project/.env` and fill in real
   values (pick your own passwords/secret key - this file is gitignored).
2. Open `starter-project/docker-compose.yml`. It has TODOs:
   - Pin `db`, `broker`, and `webserver` to exact version tags instead of the
     placeholders shown (check Docker Hub / GHCR for current release tags).
   - Wire `env_file: .env` onto both `db` and `webserver` (paperless and
     postgres need to agree on the same credentials).

## Verify

```bash
cd starter-project
cp .env.example .env   # then edit it
docker compose config  # confirm no floating tags, no secrets leaked in compose itself
docker compose up
```

UI at http://localhost:8000. Confirm the stack comes up cleanly from registry
images only - no local build step should run.

## Teardown

```bash
docker compose down -v
```
