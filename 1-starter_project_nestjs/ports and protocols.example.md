# Ports and Protocols

| Service                      | Docker Port | Development Ports | Kubernetes Ingress                          | Protocol |
|------------------------------|-------------|-------------------|---------------------------------------------|----------|
| **thesetags.api**            | 5101        | 5001              | `api.heatmaply.local:8443`                  | HTTPS    |
| **thesetags.messaging**      | 5102        | 5002              | –                                           | HTTP     |
| **thesetags.web**            | 3101        | 3001              | `web.heatmaply.local:8443`                  | HTTPS    |
| **Postgres**                 | 5432        | –                 | IngressRouteTCP (Traefik `postgres` ep)     | TCP      |
| **RabbitMQ**                 | 5672        | –                 | –                                           | AMQP     |
| **RabbitMQ Management UI**   | 15672       | –                 | `rabbitmq.heatmaply.local:8080`             | HTTP     |
| **Minio**                    | 9000        | –                 | –                                           | HTTP     |
| **Minio Console**            | 9090        | –                 | `minio.heatmaply.local:8080`                | HTTP     |
| **Blackbox Exporter**        | 9115        | –                 | –                                           | HTTP     |
| **Grafana**                  | 3000        | –                 | –                                           | HTTP     |
| **Prometheus**               | 9091        | –                 | –                                           | HTTP     |
| **Postgres Exporter**        | 9187        | –                 | –                                           | HTTP     |
