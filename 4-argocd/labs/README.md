# Lab - Paperless-ngx via Argo CD

Independent practice for this stage's concepts: an Argo CD `Application`
syncing the same paperless-ngx manifests from the stage 3 lab, but now with
sync-wave ordering instead of numbered filenames, plus a drift/self-heal demo.

Namespace: `paperless-lab` (same as the stage 3 lab - separate from
`mytravels-default`). This is the GitOps version of that same stack: the
manifests are near-identical, just re-annotated for Argo CD the way this
course's own `3-kubernetes/manifests/` → `4-argocd/manifests/` migration did
(numbered files → `argocd.argoproj.io/sync-wave` annotations).

## Your task

1. `manifests/postgres/` and `manifests/redis/` are already annotated
   `sync-wave: "0"` - nothing to do there.
2. `manifests/paperless/*.yaml` have **no** sync-wave annotation yet (so they
   default to wave `0`, the same wave as postgres/redis). Add
   `argocd.argoproj.io/sync-wave: "1"` to each, so paperless only syncs once
   Argo reports postgres and redis `Healthy` - not just "applied."
3. `manifests/ingress.yaml` is in the same situation - give it wave `"2"`.
4. `argocd/application.yaml` has two TODOs: `repoURL` (point it at your own
   fork/remote - Argo CD clones from git, it won't see local uncommitted
   changes) and `path` (point it at this lab's `manifests/` folder).

This lab's `Application` reuses Argo CD's built-in `default` AppProject rather
than a dedicated one like the main course's `mytravels` AppProject - that's a
deliberate shortcut to keep the lab focused on sync-waves and self-heal, not
project scoping.

## Verify

```bash
git add . && git commit -m "paperless lab manifests" && git push
kubectl apply -f argocd/application.yaml
argocd app get paperless-lab   # or check the Argo CD UI
```

- Watch the UI/CLI: postgres and redis should go `Healthy` before paperless
  even starts syncing. If paperless starts at the same time as postgres, your
  sync-wave annotations aren't taking effect - check the TODOs above.
- Once `Synced`/`Healthy`, http://paperless.lab.local should load.

**Drift and self-heal** (same idea as the main runbook's step of the same
name):

```bash
kubectl scale deployment/paperless -n paperless-lab --replicas=2
```

- With `selfHeal: false` (the default in the provided `application.yaml`),
  Argo CD shows `OutOfSync` and leaves your manual change alone.
- Flip `syncPolicy.automated.selfHeal` to `true`, re-apply the `Application`,
  and repeat the `kubectl scale` - Argo CD should revert it back to `replicas: 1`
  within seconds.

## Teardown

```bash
kubectl delete -f argocd/application.yaml
kubectl delete namespace paperless-lab
```
