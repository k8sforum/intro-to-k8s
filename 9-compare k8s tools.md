
# Tools

Local Kubernetes tools for development and learning. All run a real Kubernetes cluster on your machine - they differ in how they do it and what trade-offs they make.

**Prerequisites:** kind and k3d need Docker; minikube needs Docker or a VM driver. You'll also want `kubectl` to talk to the cluster.

## Comparison

| | minikube | kind | k3d |
|---|---|---|---|
| **Runs via** | VM or Docker | Docker containers | Docker containers (k3s) |
| **Multi-node** | Yes (`--nodes`, HA via `--ha`) | Yes | Yes |
| **Speed to start** | Slow (VM boot) | Fast | Very fast |
| **Resource usage** | High (VM overhead) | Medium | Low (k3s is lightweight) |
| **Kubernetes dist** | Upstream K8s | Upstream K8s | k3s (lightweight K8s) |
| **Best for** | Learning, full K8s feature parity | CI pipelines, multi-node testing | Local dev, fast iteration |
| **Platform support** | Mac, Windows, Linux | Mac, Windows, Linux | Mac, Windows, Linux |

## minikube
- The most beginner-friendly option - has a dashboard, addons system, and good documentation
- Spins up a VM (or Docker container) with a full cluster - single-node by default, multi-node with `--nodes`, multiple control planes with `--ha`
- Slower to start than the others due to VM overhead when not using the Docker driver
- Addons make it easy to enable ingress, metrics-server, registry, etc. with one command
- **Use when:** learning Kubernetes for the first time or needing full upstream K8s feature parity locally
- **Quick start:** `brew install minikube` then `minikube start`
- **Website:** https://minikube.sigs.k8s.io

## kind (Kubernetes IN Docker)
- Runs each K8s node as a Docker container - no VM required
- Originally built for testing Kubernetes itself; widely used in CI/CD pipelines
- Supports multi-node clusters (multiple control planes + workers) defined in a config file
- No built-in dashboard or addon system - more manual setup required
- **Use when:** writing CI pipelines that need a real cluster, or testing multi-node behaviour
- **Quick start:** `brew install kind` then `kind create cluster`
- **Website:** https://kind.sigs.k8s.io

## k3d
- Wraps **k3s** (Rancher's lightweight Kubernetes distro) inside Docker containers
- Fastest startup of the three - clusters spin up in seconds
- k3s omits some rarely-used K8s features (e.g. cloud-provider integrations) to stay lean
- Great `k3d` CLI for creating, deleting, and managing multiple clusters quickly
- **Use when:** local development where fast iteration matters and you don't need every upstream K8s feature
- **Quick start:** `brew install k3d` then `k3d cluster create`
- **Website:** https://k3d.io
