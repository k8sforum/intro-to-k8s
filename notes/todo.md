## What is Kubernetes?
- Container orchestration tool (manages containerized applications)
- Manage them in cloud, vm, physical machines or hybrid

## What problems does Kubernetes solve?

Managing applications at scale is HARD. 

- AVAILABILITY - App crashes - Self Healing
- SCALABILITY - Scaling and load balancing - 
- DISASTER RECOVER 

## L1 - Basic k8s Cluster Architecture

- Cluster
    - Control Plane (API Server, Controller Manager, Scheduler, ETCD)
    - Worker Nodes (kubelet, kubeproxy)
- Nodes (Runs pods / workloads)

## L2

- Namespaces    
    - default
    - kube-public
    - kube- system

## L2

- Workloads
    - Pods (runs containers)
    - Jobs
    - CronJobs
- Networking
    - Service
    - Endpoints
    - Ingress
    - Ingress Controller
- Storage
    - Persistent Volume
    - Persistent Volume Claim
    - Storage Class
- Config
    - Config Maps
    - Secrets
- Observability
    -
    - 
- Security
    -
    - 
