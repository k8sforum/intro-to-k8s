# Lab - Paperless-ngx on Kubernetes

Independent practice for this stage's concepts: Namespaces, Secrets, ConfigMaps,
PVCs, Deployments/Services, and Ingress via the Traefik already configured for
MyTravels - applied to the paperless-ngx stack from the earlier labs.

Namespace: `paperless-lab` (deliberately separate from `mytravels-default`, so
this doesn't collide with your MyTravels deployment running alongside it).
Ingress host: `paperless.lab.local` - add it to `/etc/hosts` the same way you
did for `*.mytravels.local` in the main runbook.

## Layout

```
manifests/
  1-namespace.yaml
  9-ingress.yaml       # applied last, like the main course's manifests
  postgres/
  redis/
  paperless/
```

Numbering mirrors the main course's convention: low numbers first, high
numbers last, unnumbered per-service folders applied in between in the order
listed below.

## Your task

Each folder below has TODOs (search for `# TODO`). Apply in this order,
checking `kubectl get pods -n paperless-lab` between steps:

1. `1-namespace.yaml` - as-is, no TODOs.
2. `postgres/` - TODO: wire the Deployment's env vars to `postgres-secret` via `secretKeyRef`.
3. `redis/` - no Secret needed, no TODOs. A sanity check for the pattern before the harder ones.
4. `paperless/` - the big one:
   - `2-configmap.yaml`: fill in `PAPERLESS_DBHOST`, `PAPERLESS_REDIS`, `PAPERLESS_URL` so they point at the Services you just created (Kubernetes Service DNS names, not `localhost`).
   - `4-deployment.yaml`: wire `envFrom` to both the ConfigMap and Secret, and mount the two PVCs at the paths paperless expects.
5. `9-ingress.yaml` - TODO: fill in the `host` and backend `service`/`port`.

## Verify

```bash
kubectl apply -f manifests/1-namespace.yaml
kubectl apply -f manifests/postgres/
kubectl apply -f manifests/redis/
kubectl apply -f manifests/paperless/
kubectl apply -f manifests/9-ingress.yaml

kubectl get pods -n paperless-lab -w
```

- All pods `Running`/`Ready`.
- http://paperless.lab.local loads the UI.
- Upload a document, `kubectl delete pod -n paperless-lab -l app=paperless`,
  confirm the document is still there once the replacement pod is ready
  (proves the PVC, not the pod, owns the data).

## Teardown

```bash
kubectl delete namespace paperless-lab
```
