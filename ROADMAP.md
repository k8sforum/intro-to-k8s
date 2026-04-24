# Kubernetes Learning Roadmap

## Understanding the Problem Space
1. Learn the why? | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=2m18s) | [Docs](https://kubernetes.io/docs/concepts/overview/)
2. The architecture | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=22m29s) | [Docs](https://kubernetes.io/docs/concepts/architecture/)
3. Core Components | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=10m43s) | [Docs](https://kubernetes.io/docs/concepts/overview/components/)

## Core Building Blocks
4. User-Defined Objects | [Docs](https://kubernetes.io/docs/concepts/overview/working-with-objects/)
5. Namespaces | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=1h30m3s) | [Docs](https://kubernetes.io/docs/concepts/overview/working-with-objects/namespaces/)
6. Networking | [Video](https://www.youtube.com/watch?v=MTHGoGUFpvE&t=2h12m2s) | [Docs](https://kubernetes.io/docs/concepts/services-networking/)
7. Storage | [Video](https://www.youtube.com/watch?v=MTHGoGUFpvE&t=2h2m) | [Docs](https://kubernetes.io/docs/concepts/storage/)

## How Workloads Actually Run
8. Deployments, Labels and Manifest Files | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=1h2m3s) | [Docs](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/)

## Debugging and Observability
9. Troubleshooting | [Docs](https://kubernetes.io/docs/tasks/debug/) | [Learn Kube](https://learnkube.com/troubleshooting-deployments)
10. Monitoring (Prometheus) | [Docs](https://kubernetes.io/docs/tasks/debug/debug-cluster/resource-metrics-pipeline/)

## Configuration and Deployment Patterns
11. Helm Charts | [Video](https://www.youtube.com/watch?v=X48VuDVv0do&t=1h51m35s) | [Docs](https://kubernetes.io/docs/concepts/cluster-administration/helm-charts/)
12. Misconfigurations | [Docs](https://kubernetes.io/docs/concepts/configuration/)
13. Adding TLS Certificates | [cert-manager concepts](https://cert-manager.io/docs/concepts/) | [Installation via Helm](https://cert-manager.io/docs/installation/helm/)

## Cloud Differences
14. Self-managed vs managed services | [Docs](https://kubernetes.io/docs/setup/)
15. Networking — install a network plugin | [Docs](https://kubernetes.io/docs/concepts/cluster-administration/addons/)

## Real World Patterns
16. Access management (RBAC) | [Docs](https://kubernetes.io/docs/reference/access-authn-authz/rbac/)
17. Backup and secure data | [Docs](https://kubernetes.io/docs/tasks/administer-cluster/securing-a-cluster/)
18. Kubernetes Operators (databases, message brokers) | [Docs](https://kubernetes.io/docs/concepts/extend-kubernetes/operator/)
19. Migrate Postgres to CloudNativePG | [cloudnative-pg.io](https://cloudnative-pg.io/)
20. Explore Cilium as a CNI

## Strategic Thinking
21. Best practices | [Docs](https://kubernetes.io/docs/concepts/configuration/overview/) | [Learn Kube](https://learnkube.com/production-best-practices)