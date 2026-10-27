Create a landscape infographic poster as a single self-contained HTML file.

## Canvas and output
- Fixed 1920 x 1080 px canvas (16:9), centered on the page, so it can be screenshotted or exported cleanly to PNG/PDF.
- No external dependencies except Google Fonts. Embed every logo inline (inline SVG preferred, base64 data URI as a fallback). Do not hotlink images.
- Each tool card must show that tool's real, official logo, fetched from the URL listed under the tool below and embedded as-is. Do not substitute a text badge, placeholder, icon or redrawn approximation.

## Visual style
- Kubernetes brand blue `#326CE5` as the primary colour, on a light background (white or very light blue-grey such as `#F5F8FD`).
- Clean, modern tech look: generous white space, rounded cards, soft shadows, no clutter.
- Fonts: Inter (or similar) for text, JetBrains Mono for code.
- Take a small accent colour for each tool from its own logo, and use it for that tool's card border/header so the three columns are easy to tell apart.
- Code blocks look like a mini terminal: dark background (`#0F172A` or similar), a `$` prompt, light monospace text, and three small window dots in the top corner.
- Put a small, subtle Kubernetes helm-wheel logo near the header.

## Layout (top to bottom)

### 1. Header band
- Category label as a pill/chip: **TOOLS**
- Heading (large, bold): **Did you know you can run a Kubernetes cluster on your own machine?**
- Subheading: **Here's how.**

### 2. Three tool cards, side by side (the main body)
Each card contains, in this order: logo, name, a one-line tagline, 3-4 short bullet points, an "Install" code block, a "Create a cluster" code block, a highlighted **Use when** line, and the website URL at the bottom.

**minikube**
- Logo: https://raw.githubusercontent.com/kubernetes/minikube/master/images/logo/logo.svg
- Tagline: The most beginner-friendly option
- Bullets:
  - Runs a full cluster in a VM or a Docker container
  - Single-node by default; add nodes with `--nodes`, or multiple control planes with `--ha`
  - Built-in dashboard and addons: enable ingress, metrics-server or a registry with one command
  - Slower to start than the others when it boots a VM
- Install: `brew install minikube`
- Create a cluster: `minikube start`
- Use when: you are learning Kubernetes for the first time, or you need full upstream Kubernetes feature parity
- Website: https://minikube.sigs.k8s.io

**kind** (Kubernetes IN Docker)
- Logo: https://raw.githubusercontent.com/kubernetes-sigs/kind/main/logo/logo.svg
- Tagline: Each Kubernetes node is a Docker container
- Bullets:
  - No VM required
  - Originally built to test Kubernetes itself, and widely used in CI/CD pipelines
  - Multi-node clusters (multiple control planes + workers) defined in a config file
  - No built-in dashboard or addons, so expect more manual setup
- Install: `brew install kind`
- Create a cluster: `kind create cluster`
- Use when: you are writing CI pipelines that need a real cluster, or testing multi-node behaviour
- Website: https://kind.sigs.k8s.io

**k3d**
- Logo: https://raw.githubusercontent.com/k3d-io/k3d/main/docs/static/img/k3d_logo_black_blue.svg
- Tagline: Lightweight k3s, wrapped in Docker
- Bullets:
  - Runs k3s (Rancher's lightweight Kubernetes distribution) inside Docker containers
  - Fastest startup of the three: clusters spin up in seconds
  - Leaves out some rarely used features (e.g. cloud-provider integrations) to stay lean
  - A friendly CLI for creating, deleting and managing several clusters
- Install: `brew install k3d`
- Create a cluster: `k3d cluster create`
- Use when: you are doing local development where fast iteration matters and you don't need every upstream feature
- Website: https://k3d.io

### 3. Comparison strip (compact table across the full width)
| | minikube | kind | k3d |
|---|---|---|---|
| Runs via | VM or Docker | Docker containers | Docker containers (k3s) |
| Multi-node | Yes (`--nodes`, `--ha`) | Yes | Yes |
| Startup speed | Slow (VM boot) | Fast | Very fast |
| Resource usage | High | Medium | Low |
| Kubernetes distribution | Upstream | Upstream | k3s (lightweight) |
| Best for | Learning, full feature parity | CI pipelines, multi-node testing | Local dev, fast iteration |

Show speed and resource usage as small visual meters (e.g. 1-3 filled dots or bars) next to the text so they can be compared at a glance.

### 4. Footer band
- Left: "You'll need: Docker (minikube can also use a VM) and kubectl to talk to the cluster."
- Bottom line, small and subtle: "All three run on macOS, Windows and Linux.Install commands shown use Homebrew (macOS). See each website for Linux and Windows."

## Rules
- Keep all text exactly as written above; do not add claims, statistics or features that aren't listed.
- Code must be real, selectable text (not images), with no typos.
- Everything must fit inside 1920 x 1080 without scrolling or overflow. If space is tight, shorten bullets before shrinking the code blocks.
- Text must stay readable at 50% zoom: body text at least 18px, headings well above that.
