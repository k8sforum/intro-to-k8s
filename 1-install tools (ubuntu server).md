# Required Tools - Ubuntu Server (k3d on the server, kubectl from your Mac)

Install notes for running the k3d cluster on a **headless Ubuntu server** and driving it from your MacBook. The server hosts the cluster; the Mac only needs `kubectl` (plus whatever else a notebook needs, see [the macOS notes](<1-install tools (macos).md>)). For a single machine that does everything, use [the Ubuntu desktop notes](<1-install tools (ubuntu).md>) instead.

```
MacBook                                  Ubuntu server
kubectl ──── https :6443 ──────────────► k3d load balancer ─► k3s API server
browser ──── http  :8080 (ingress) ────► k3d load balancer ─► Traefik ─► app
```

k3d runs k3s inside Docker containers, and the Kubernetes API is plain HTTPS on a published port. Nothing about k3d is local-only: kubectl just needs a kubeconfig that points at the server instead of `localhost`.

## Quick reference

| Machine | Tool | Purpose | Install |
|---|---|---|---|
| Server | Docker Engine | Container runtime k3d runs on | `curl -fsSL https://get.docker.com \| sudo sh` |
| Server | k3d | Creates the k3s cluster | `curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh \| bash` |
| Server | kubectl (optional) | Debugging from the server itself | `sudo snap install kubectl --classic` |
| Mac | kubectl | Talks to the remote cluster | `brew install kubectl` |
| Mac | Freelens (optional) | Cluster GUI, reads the same kubeconfig | `brew install --cask freelens` |

Keep the Mac's kubectl within one minor version of the cluster (the runbooks pin k3s `v1.35.x`).

## On the server

### Docker Engine

Rancher Desktop is a desktop app and does not belong on a headless box. Use Docker's own packages:

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"
```

Log out and back in so the group change applies, then confirm with `docker ps`.

### k3d

```bash
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash
```

If `sudo` has no terminal (non-interactive shell), install into a user-owned directory instead:

```bash
mkdir -p "$HOME/.local/bin"
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | USE_SUDO=false K3D_INSTALL_DIR="$HOME/.local/bin" bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
```

## Create the cluster

Create it by hand on the server, with the API port pinned so the Mac knows where to connect. The Mac reaches the cluster directly over the LAN or a VPN, with no tunnel.

> **Docker bypasses `ufw`.** Ports that Docker publishes are routed through Docker's own iptables chains, so `ufw allow` / `ufw deny` rules do not reliably restrict them, and `ufw deny 6443` will not hide the API. What you bind the port to is the control that works. That is why the command below binds to a specific address rather than relying on the firewall.

Bind to the server's LAN (or Tailscale/WireGuard) address, never `0.0.0.0` on an internet-facing host, and add that address to the API certificate so kubectl's TLS check passes.

```bash
SERVER_IP=192.168.1.50   # the address your Mac reaches the server on

k3d cluster create k8scluster \
  --api-port "${SERVER_IP}:6443" \
  --k3s-arg "--tls-san=${SERVER_IP}@server:*" \
  -p "${SERVER_IP}:8080:80@loadbalancer" \
  -p "127.0.0.1:5432:5432@loadbalancer" \
  --image ghcr.io/k3s-io/k3s:v1.35.3-k3s1 \
  --servers 1 \
  --agents 3
```

The image and node counts match [3-kubernetes/runbook.ipynb](3-kubernetes/runbook.ipynb). Postgres stays on loopback; reach it with `kubectl port-forward` when you need it.

> **`--tls-san` can only be set at creation.** If kubectl later fails with `x509: certificate is valid for ..., not <your-ip>`, the cluster was created without it. Run `k3d cluster delete k8scluster` and recreate it. If you use a hostname rather than an IP, put the hostname in `--tls-san`.

## Connect from the Mac

Copy the kubeconfig over and point it at the server:

```bash
# on the server
k3d kubeconfig get k8scluster > k8scluster.yaml

# on the Mac
mkdir -p ~/.kube
scp USER@SERVER_IP:k8scluster.yaml ~/.kube/k8scluster-remote.yaml
```

Edit the `server:` line in `~/.kube/k8scluster-remote.yaml` to point at the server (k3d writes `https://0.0.0.0:6443` or similar):

```bash
SERVER_IP=192.168.1.50   # the address your Mac reaches the server on
sed -i '' "s#server: https://.*#server: https://${SERVER_IP}:6443#" ~/.kube/k8scluster-remote.yaml
export KUBECONFIG=~/.kube/k8scluster-remote.yaml
```

Verify:

```bash
kubectl cluster-info
kubectl get nodes
```

You should see four nodes (`k3d-k8scluster-server-0` and three agents). `~/.kube/k8scluster-remote.yaml` contains cluster-admin credentials: keep it out of git and `chmod 600` it.

If `KUBECONFIG` is not exported, kubectl and Freelens use `~/.kube/config`. To make the remote cluster permanent there, merge it: `KUBECONFIG=~/.kube/config:~/.kube/k8scluster-remote.yaml kubectl config view --flatten > ~/.kube/config.merged && mv ~/.kube/config.merged ~/.kube/config`.

## Running the runbooks against the remote cluster

- **Skip the cluster-create cell.** The stage runbooks guard creation by looking for a cluster named `k8scluster` in `k3d cluster list`, which only sees clusters on the machine it runs on. On the Mac that check fails and the cell would try to create a second, local cluster. You already created the real one above.
- **`kubectl apply`, `get`, `logs`, `exec`, `port-forward` work unchanged.** They all go through the API server.
- **Point the `mytravels.local` entries in the Mac's `/etc/hosts` at `SERVER_IP`** instead of `127.0.0.1`, then use the `*.mytravels.local:8080` URLs as the runbooks show them. `localhost:8080` on the Mac does not reach the cluster, because the ingress port is bound to `SERVER_IP` only.
- **Run one stage at a time**, same as locally: stages 3 to 6 share the cluster name `k8scluster` and the same ports.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `x509: certificate is valid for ..., not <ip>` | The cluster was created without `--tls-san`. Delete and recreate. |
| `connection refused` on `SERVER_IP:6443` | The cluster is bound to a different address, or Docker is not running. Check `docker ps` on the server for the `k3d-k8scluster-serverlb` container and its port mapping. |
| `Cannot connect to the Docker daemon` on the server | Docker is not running, or the group change has not applied yet. Log out and back in. |
| kubectl works but the browser gets `404 page not found` on `:8080` | The request reached Traefik with a hostname no Ingress matches. Use the `*.mytravels.local` hostnames from the runbook, not the bare IP. |
