# Required Tools — Ubuntu / Debian

Install notes for the tools used across the runbooks in this repo. Each notebook's Prerequisites cell links back here and only lists which of these tools it actually needs — this file has the install commands and any gotchas.

## Quick reference

| Tool | Purpose | Install |
|---|---|---|
| Rancher Desktop | Docker engine + container registry | apt repo — see below |
| Docker / Docker Compose | Container runtime + compose CLI | bundled with Rancher Desktop — see below |
| k3d | Local Kubernetes cluster (used in `3-kubernetes`) | `curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh \| bash` |
| kubectl | Kubernetes CLI | `sudo snap install kubectl --classic` |
| JupyterLab | Run these notebooks | `sudo apt install -y pipx && pipx install jupyterlab` |
| Node.js / npm | Run the Web app from source (used in `0-local`) | NodeSource repo — see below |
| Freelens (or OpenLens) | Kubernetes cluster GUI (optional) | `sudo snap install freelens --classic` |

## Rancher Desktop

Rancher Desktop isn't in the default Ubuntu repos — it ships its own APT repo:

```bash
sudo apt install -y curl

curl -s https://download.opensuse.org/repositories/isv:/Rancher:/stable/deb/Release.key | gpg --dearmor | sudo dd status=none of=/usr/share/keyrings/isv-rancher-stable-archive-keyring.gpg
echo 'deb [signed-by=/usr/share/keyrings/isv-rancher-stable-archive-keyring.gpg] https://download.opensuse.org/repositories/isv:/Rancher:/stable/deb/ ./' | sudo dd status=none of=/etc/apt/sources.list.d/isv-rancher-stable.list

sudo apt update
sudo apt install rancher-desktop
```

Once Rancher Desktop is installed, open it and ensure the container engine is running before continuing.

## Docker / Docker Compose

Rancher Desktop provides both, but on Linux it does **not** install a system-wide `docker` package.

> **`docker` command not found after installing Rancher Desktop:** there is no `docker` on apt or a working `snap install docker`. Rancher Desktop ships its own CLI, symlinked into `~/.rd/bin/docker`, and its installer appends a block to `~/.bashrc` (`### MANAGED BY RANCHER DESKTOP START`) that puts `~/.rd/bin` on `PATH`. That change only applies to shells opened *after* Rancher Desktop's first launch — your current terminal (and any already-running Jupyter kernel) won't see it. Open a new terminal (or `source ~/.bashrc`) and confirm Rancher Desktop itself has fully started (tray icon, or `pgrep -f rancher-desktop`), then retry `docker --version`. Don't additionally run `sudo snap install docker` or `sudo apt install docker.io` — those pull in an unrelated Docker engine that conflicts with the one Rancher Desktop manages.

If you'd rather run a standalone Docker Engine instead of Rancher Desktop (e.g. on a headless dev box), use Docker's own apt repo:

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"
```

This installs the `docker` CLI, engine, and the `docker compose` plugin together — log out and back in for the group change to take effect.

## k3d

```bash
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash
```

> **k3d install without sudo:** the official install script installs into `/usr/local/bin` via `sudo`, which fails in non-interactive shells (`sudo: a terminal is required to authenticate`). If that happens, install into a user-owned directory instead:
> ```bash
> mkdir -p "$HOME/.local/bin"
> curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | USE_SUDO=false K3D_INSTALL_DIR="$HOME/.local/bin" bash
> echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
> ```
> Open a new shell (or `source ~/.bashrc`) afterward so `k3d` is on `PATH`.

## kubectl

```bash
sudo snap install kubectl --classic
```

> **kubectl via apt:** `sudo apt install -y kubectl` fails on stock Ubuntu with `Unable to locate package kubectl` — there's no apt package, only a snap. The `--classic` flag above is required since kubectl needs unconfined filesystem access to read kubeconfig files. If you'd rather use apt, add Kubernetes's own apt repo first:
> ```bash
> curl -fsSL https://pkgs.k8s.io/core:/stable:/v1.35/deb/Release.key | sudo gpg --dearmor -o /etc/apt/keyrings/kubernetes-apt-keyring.gpg
> echo 'deb [signed-by=/etc/apt/keyrings/kubernetes-apt-keyring.gpg] https://pkgs.k8s.io/core:/stable:/v1.35/deb/ /' | sudo tee /etc/apt/sources.list.d/kubernetes.list
> sudo apt update
> sudo apt install -y kubectl
> ```

## JupyterLab

```bash
sudo apt install -y pipx
pipx install jupyterlab
```

> **JupyterLab via pip:** `pip install jupyterlab` fails on stock Ubuntu with `Command 'pip' not found` — recent Ubuntu images don't ship `pip` by default. Installing it via `sudo apt install python3-pip` then works, but a plain `pip install jupyterlab` afterward hits Debian/Ubuntu's PEP 668 guard (`error: externally-managed-environment`), since the system Python is protected from arbitrary global installs. `pipx` sidesteps this entirely — it installs JupyterLab into its own isolated venv and puts the `jupyter-lab` command on your `PATH`, which is exactly what you want for a CLI tool like this.

## Node.js / npm

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
sudo apt install -y nodejs
```

> **Don't use the default apt `nodejs` package:** `sudo apt install nodejs` pulls whatever version shipped with your Ubuntu release — often several major versions behind — which is too old for this repo's Vite 8 / React 19 toolchain. The NodeSource script above adds their apt repo pinned to the Node 22 LTS line (matching `src/web/Dockerfile`) and installs `nodejs` and `npm` together.

Confirm with `node --version` and `npm --version`.

## Freelens / OpenLens (optional)

A desktop GUI for browsing the cluster (pods, logs, exec, metrics) as an alternative to raw `kubectl`. Not required by any notebook cell — purely a convenience.

> **OpenLens has no snap:** `sudo snap install openlens` fails with `snap "openlens" not found` — there is no `openlens` snap. Use **Freelens** instead:
> ```bash
> sudo snap install freelens --classic
> ```
> Freelens is the actively maintained open-source continuation of Lens/OpenLens now that upstream Lens has gone closed-source and OpenLens has stopped receiving releases; it's functionally the same GUI. If you'd rather stick with the original OpenLens, grab the `.deb`/`.AppImage` from the [OpenLens releases page](https://github.com/MuhammedKalkan/OpenLens/releases) instead.

After a k3d cluster is created, either app picks up its context automatically from your default kubeconfig (`~/.kube/config`).
