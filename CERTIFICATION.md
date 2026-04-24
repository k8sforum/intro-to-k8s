# Certification Path

Certifications are offered by the **[CNCF (Cloud Native Computing Foundation)](https://www.cncf.io/)** and administered by the **[Linux Foundation](https://training.linuxfoundation.org/)**. All hands-on exams run in a live terminal — no multiple choice.

## Progression

```
KCNA ──► CKAD ──► CKA ──► CKS
                         (requires active CKA)
```

## Quick Reference

| Cert | Level | Type | Cost | Duration |
|------|-------|------|------|----------|
| KCNA| Associate | Multiple choice | ~$250 | — |
| CKAD| Developer | Hands-on | ~$395 | 2 hrs |
| CKA | Admin | Hands-on | ~$395 | 2 hrs |
| CKS | Security | Hands-on | ~$395 | 2 hrs |

---

## KCNA — Kubernetes and Cloud Native Associate

- **Focus:** Conceptual understanding of Kubernetes, cloud native principles, containers, CI/CD, observability, and the CNCF landscape
- **Good for:** Validating foundational knowledge before hands-on certifications
- **Curriculum:** [KCNA_Curriculum.pdf](https://github.com/cncf/curriculum/blob/master/KCNA_Curriculum.pdf)

## CKAD — Certified Kubernetes Application Developer

- **Focus:** Deploying and configuring applications — pods, deployments, services, config maps, resource limits, probes, jobs, ingress, network policies, Helm
- **Good for:** Developers who build and ship apps onto K8s clusters
- **Curriculum:** [CKAD_Curriculum.pdf](https://github.com/cncf/curriculum/blob/master/CKAD_Curriculum.pdf)

## CKA — Certified Kubernetes Administrator

- **Focus:** Cluster setup and administration — kubeadm, CNI plugins, PV/PVC, RBAC, node maintenance, troubleshooting cluster components, etcd backup and restore
- **Good for:** Platform/infrastructure engineers managing clusters
- **Curriculum:** [CKA_Curriculum.pdf](https://github.com/cncf/curriculum/blob/master/CKA_Curriculum.pdf)

## CKS — Certified Kubernetes Security Specialist

- **Prerequisite:** Active CKA certification
- **Focus:** Hardening clusters and workloads — supply chain security, network policies, pod security standards, secrets management, runtime security (Falco), image scanning, audit logging
- **Good for:** Anyone responsible for securing production Kubernetes environments
- **Curriculum:** [CKS_Curriculum.pdf](https://github.com/cncf/curriculum/blob/master/CKS_Curriculum.pdf)

---

## Tips

**Studying**
- Practice `kubectl` daily — the exam is a live terminal and speed matters
- Bookmark the [Kubernetes docs](https://kubernetes.io/docs/) — the only resource allowed during the exam
- Use `kubectl explain <resource>` and `kubectl --help` heavily during practice

**During the exam**
- Flag hard questions and return to them — time management is critical
- Each exam comes with **one free retake** if purchased through the Linux Foundation
