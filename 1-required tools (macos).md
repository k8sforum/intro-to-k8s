# Required Tools — macOS

Install notes for the tools used across the runbooks in this repo. Each notebook's Prerequisites cell links back here and only lists which of these tools it actually needs — this file has the install commands and any gotchas.

## Quick reference

| Tool | Purpose | Install |
|---|---|---|
| Rancher Desktop | Docker engine + `docker compose` + container registry | `brew install --cask rancher` |
| Docker / Docker Compose | Container runtime + compose CLI | bundled with Rancher Desktop — see below |
| k3d | Local Kubernetes cluster (used in `3-kubernetes`) | `brew install k3d` |
| kubectl | Kubernetes CLI | `brew install kubectl` |
| JupyterLab | Run these notebooks | `brew install jupyterlab` |
| Freelens (or OpenLens) | Kubernetes cluster GUI (optional) | `brew install --cask freelens` |

## Rancher Desktop

```bash
brew install --cask rancher
```

Open it and ensure the container engine is running before continuing — the menu bar icon shows a spinner until it's ready. Rancher Desktop bundles the `docker` CLI and `docker compose` v2 plugin, so a separate Docker install isn't needed for the notebooks in this repo.

## Docker / Docker Compose

Rancher Desktop already provides both `docker` and `docker compose`. Only install these separately if you specifically want Docker Desktop instead of Rancher Desktop, or a CLI-only setup:

```bash
# Docker Desktop (GUI, alternative to Rancher Desktop)
brew install --cask docker

# CLI-only, no GUI — needs a running engine such as Colima
brew install docker docker-compose colima
colima start
```

Don't run Rancher Desktop and Docker Desktop at the same time — they both try to own the `docker` context and will conflict.

## k3d

```bash
brew install k3d
```

Used to create the local multi-node Kubernetes cluster in `3-kubernetes`. Requires Rancher Desktop (or another Docker engine) to be running first.

## kubectl

```bash
brew install kubectl
```

## JupyterLab

```bash
brew install jupyterlab
```

## Freelens / OpenLens (optional)

A desktop GUI for browsing the cluster (pods, logs, exec, metrics) as an alternative to raw `kubectl`. Not required by any notebook cell — purely a convenience.

```bash
brew install --cask freelens
```

**Use Freelens, not OpenLens.** OpenLens's last release was June 2023 and it's no longer maintained. Freelens is the actively maintained open-source continuation of the same GUI and is a drop-in replacement. If you want OpenLens anyway: `brew install --cask openlens`.

After a k3d cluster is created, either app picks up its context automatically from your default kubeconfig (`~/.kube/config`).
