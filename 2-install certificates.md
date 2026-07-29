# Adding certificates

TLS in Kubernetes is managed through **cert-manager** — a controller that watches `Certificate` resources, issues them via a configured `ClusterIssuer`, and stores the result in a `Secret`. Traefik (and any other ingress controller) picks up that Secret via the `tls.secretName` field on an `Ingress` resource.

```
ClusterIssuer → Certificate → Secret (tls.crt / tls.key) ← Ingress.tls.secretName
```

## Reading material
| Topic | Resource |
|---|---|
| cert-manager concepts | [cert-manager.io/docs/concepts](https://cert-manager.io/docs/concepts/) |
| Installation via Helm | [cert-manager.io/docs/installation/helm](https://cert-manager.io/docs/installation/helm/) |
| ClusterIssuers | [cert-manager.io/docs/configuration](https://cert-manager.io/docs/configuration/) |
| Self-signed CA issuer | [cert-manager.io/docs/configuration/ca](https://cert-manager.io/docs/configuration/ca/) |
| Let's Encrypt ACME | [cert-manager.io/docs/configuration/acme](https://cert-manager.io/docs/configuration/acme/) |
| Traefik + cert-manager integration | [doc.traefik.io/traefik/providers/kubernetes-ingress](https://doc.traefik.io/traefik/providers/kubernetes-ingress/) |
| TechWorld with Nana — cert-manager full tutorial | [youtube.com/watch?v=DvXkD0f-lhY](https://www.youtube.com/watch?v=DvXkD0f-lhY) |
| mkcert — local CA without a controller | [github.com/FiloSottile/mkcert](https://github.com/FiloSottile/mkcert) |
