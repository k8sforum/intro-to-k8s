# Required Tools — Windows

Install notes for the tools used across the runbooks in this repo. Each notebook's Prerequisites cell links back here and only lists which of these tools it actually needs — this file has the install commands and any gotchas.

All commands use `winget`, which ships built in on Windows 10 (1809+) and Windows 11. Run them from PowerShell.

## Quick reference

| Tool | Purpose | Install |
|---|---|---|
| Rancher Desktop | Docker engine + `docker compose` + container registry | `winget install --id SUSE.RancherDesktop -e` |
| Docker / Docker Compose | Container runtime + compose CLI | bundled with Rancher Desktop — see below |
| k3d | Local Kubernetes cluster (used in `3-kubernetes`) | `winget install --id k3d.k3d -e` |
| kubectl | Kubernetes CLI | `winget install --id Kubernetes.kubectl -e` |
| JupyterLab | Run these notebooks | `winget install --id ProjectJupyter.JupyterLab -e` |
| Freelens (or OpenLens) | Kubernetes cluster GUI (optional) | `winget install --id Freelensapp.Freelens -e` |

## Rancher Desktop

```powershell
winget install --id SUSE.RancherDesktop -e
```

Rancher Desktop on Windows runs its Linux container engine inside WSL2, so you need WSL2 enabled (`wsl --install` if you haven't already, then reboot). On first launch it walks you through picking a container engine (choose **moby/dockerd**, not containerd, to match the commands used in these notebooks) and adds `docker`, `kubectl`, and `nerdctl` to your user `PATH`. Open it and wait for the whale/tray icon to show it's running before continuing.

> **Commands not found right after install:** the `PATH` update only takes effect in terminals opened after Rancher Desktop's first launch. Close and reopen PowerShell (or your terminal) and confirm Rancher Desktop has fully started, then retry `docker --version`.

## Docker / Docker Compose

Rancher Desktop already provides both `docker` and `docker compose`. No separate install is needed.

## k3d

```powershell
winget install --id k3d.k3d -e
```

Used to create the local multi-node Kubernetes cluster in `3-kubernetes`. Requires Rancher Desktop (or another Docker engine) to be running first, and talks to it over the same named pipe Rancher Desktop's own `docker` CLI uses — no WSL-specific setup needed beyond what Rancher Desktop already configured.

## kubectl

```powershell
winget install --id Kubernetes.kubectl -e
```

Rancher Desktop already puts a working `kubectl` on `PATH`; only install this separately if you want a specific `kubectl` version pinned independently of Rancher Desktop's.

## JupyterLab

```powershell
winget install --id ProjectJupyter.JupyterLab -e
```

If you'd rather keep it isolated from your system Python (recommended if you already use Python for other projects), install via `pipx` instead:

```powershell
winget install --id Python.Python.3.12 -e
python -m pip install --user pipx
python -m pipx ensurepath
# open a new terminal, then:
pipx install jupyterlab
```

## Freelens / OpenLens (optional)

A desktop GUI for browsing the cluster (pods, logs, exec, metrics) as an alternative to raw `kubectl`. Not required by any notebook cell — purely a convenience.

```powershell
winget install --id Freelensapp.Freelens -e
```

**Use Freelens, not OpenLens.** OpenLens's last release was June 2023 and it's no longer maintained. Freelens is the actively maintained open-source continuation of the same GUI and is a drop-in replacement. If you want OpenLens anyway: `winget install --id MuhammedKalkan.OpenLens -e`.

After a k3d cluster is created, either app picks up its context automatically from your default kubeconfig (`%USERPROFILE%\.kube\config`).
