# Lab - Dockerize Paperless-ngx

Independent practice for the concepts in this stage's `runbook.ipynb` (Steps 2-7:
environment setup, build/run, service verification, diagnostics), applied to a
different app: [paperless-ngx](https://github.com/paperless-ngx/paperless-ngx), a
self-hosted document management system.

## The app

Paperless-ngx is a document scanner/archive: you feed it PDFs or images, it OCRs
them and makes them searchable. Its stack:

| Service | Role |
|---|---|
| `db` | PostgreSQL - stores document metadata |
| `broker` | Redis - task queue for the OCR/consume worker built into `webserver` |
| `webserver` | The paperless-ngx app itself (UI + API + worker) |
| `gotenberg` | Converts office documents (docx, xlsx, ...) to PDF |
| `tika` | Extracts text/metadata from office documents |

`gotenberg`+`tika` are optional in paperless-ngx but included here on purpose -
they're two more containers to wire into the network and give you a 5-service
compose file instead of a 3-service one, closer in shape to MyTravels.

## Your task

Open `starter-project/docker-compose.yml`. It has 6 TODOs. Fill them in so that:

1. `webserver` doesn't start handling requests before `db` and `broker` report healthy.
2. `webserver` can reach Postgres and Redis by their service names.
3. Office document conversion works (`gotenberg`/`tika` wired up).
4. Documents and the search index survive `docker compose down` (not `-v`).

Don't peek at `completed-project/` until you've had a real attempt.

## Verify

```bash
cd starter-project
docker compose up --build
```

- UI at http://localhost:8000 (default login `admin` / `admin`)
- Upload a PDF, confirm it gets OCR'd and becomes searchable
- Upload a `.docx`, confirm it converts and its text is searchable (proves gotenberg+tika are wired correctly)
- `docker compose down && docker compose up`, confirm your documents are still there (proves the named volumes are wired correctly)
- `docker compose logs webserver` should show no connection errors to `db`/`broker` at startup

## Teardown

```bash
docker compose down -v   # -v also removes the named volumes
```
