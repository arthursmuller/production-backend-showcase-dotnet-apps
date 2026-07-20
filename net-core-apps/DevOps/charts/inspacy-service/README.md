# Inspacy service Helm chart

This chart deploys one service release as independent `Api`, `ConsumerWorker`,
and `JobWorker` Deployments plus a one-shot `Migration` Job. Install it once per
service so a Feed rollout cannot change Comment, Following, Interaction, or
Moderation capacity.

The default values render no workloads. An environment overlay must enable the
service and provide an immutable `repository@sha256:digest`, a different
pre-created secret name for every role, exact dependency destinations, and the
provider-specific workload-identity annotations. The chart never creates or
contains a secret value. Rendering fails when enabled roles reuse a Secret, when
the migration has no dedicated dependency destination, or when an ingress
namespace selector is not paired with a pod selector. The chart targets
Kubernetes 1.31 or newer so its lifecycle and security fields are not silently
dropped by an older API server.

## Required secret contract

Each referenced Kubernetes Secret contains .NET environment-variable keys. At
minimum, runtime roles normally require:

- `Database__MariaDbSqlConnection`, using TLS verification, a five-second
  connection timeout, a 30-second command timeout, and the role's approved
  `MaximumPoolSize` from `DevOps/operational-baselines/capacity-envelope.yaml`;
- `Broker__RabbitMq__host`, `Broker__RabbitMq__port` (5671),
  `Broker__RabbitMq__username`, `Broker__RabbitMq__password`,
  `Broker__RabbitMq__virtualHost`, and the TLS server name for broker-enabled
  roles;
- `Identity__Authority` and `Identity__ClientSecret` for roles that validate or
  request identities;
- service-specific material such as Comment's cursor key, Interaction's view
  HMAC key and Redis connection, or storage credentials. Feed's API Secret and
  each Interaction runtime Secret provide `Cache__Redis__ConnectionString`;
  the committed production examples keep non-secret Redis settings only on the
  roles that actually use the cache.

The migration Secret must contain a scoped DDL identity and must not be mounted
into any runtime Deployment. API, consumer, and job secrets use DML-only
identities with only the tables/actions that role requires. Secret creation and
rotation belong to the selected external secret/workload-identity provider.
ApiHost selects and validates `Runtime:Role` before secret resolution, and each
`SecretProvider:References` alias carries an explicit `Roles` allow-list. The
provider fetches only references authorized for that process; a Migration pod,
for example, cannot fetch API identity, broker, cache, storage, or signing secrets.

When a role resolves placeholders through the mTLS secret API, set its
`certificateSecretKey` to the PKCS#12 key in that role's existing Kubernetes
Secret (the production examples use `secret-provider-client.pfx`). The chart
mounts only that item at
`/var/run/secrets/inspacy/secret-provider/client.pfx`, mode `0440`, and sets the
non-secret `SecretProvider__Certificate__Path`; the pod `fsGroup` supplies the
read boundary for the non-root process. If the bundle is encrypted, put
`SecretProvider__Certificate__Password` in the same role Secret so `envFrom`
supplies it. The application loads the key ephemerally and fails startup if the
file is missing, broadly permissioned, expired, lacks a private key/client-auth
usage, or has the wrong password. Never commit the bundle or password.

## Network and rollout contract

Network policy defaults to DNS-only runtime egress and no ingress. Configure
exact in-cluster namespace/pod selectors and provider CIDRs; the schema rejects
universal IPv4 and IPv6 CIDRs. API ingress requires both the selected edge or
monitoring namespace and the selected pods. Additional service callers must be
listed explicitly in `apiIngress` with namespace, pod, and port selectors.
Workers accept only selected monitoring pods. Migration always gets deny-all ingress and only DNS plus
`migrationClusterEgress`/`migrationExternalEgress`; it never inherits runtime
database, broker, cache, identity, or object-storage paths. Its policy carries
Helm hook annotations only when the migration Job is a hook.

`clusterEgress`/`externalEgress` select only API, consumer, and job pods. Put
role-only dependencies in the matching `api`, `consumer`, or `job` cluster and
external egress lists. Kubernetes NetworkPolicies are additive, so the cluster
CNI must enforce both ingress and egress and the environment must test every
allow and deny path after admission-controller mutations.

For a pre-install/pre-upgrade migration, the hook ServiceAccount and
NetworkPolicy use lower weights than the Job and `before-hook-creation` cleanup.
They intentionally remain available until the Job completes; applying
`hook-succeeded` to either dependency would let Helm remove it before starting
the later-weighted Job. Helm does not manage hook resources like ordinary release
objects, so an uninstall runbook must remove the dormant migration ServiceAccount
and NetworkPolicy after confirming that no migration pod exists.

Every Deployment has independent replicas, resources, ServiceAccount, Secret,
PDB, and autoscaling settings. When native HPA is enabled, the Deployment omits
`spec.replicas` so Helm upgrades cannot fight the autoscaler. API and consumer
defaults use container-scoped CPU/memory signals; JobWorker HPA remains disabled
until an approved bounded due-work/lease metric is supplied. Every HPA has a
finite `maxReplicas` and stabilization windows. External backlog/latency metrics,
their adapter, or KEDA's operator/`ScaledObject` are cluster-specific reviewed
IaC; do not let KEDA and this chart's HPA own the same Deployment.

The chart also sets requests and limits, non-root UID/GID, read-only root
filesystem, RuntimeDefault seccomp/AppArmor, dropped capabilities, disabled
service links and service-account token mounts by default. Zone spreading is a
hard three-domain constraint (`minDomains: 3`) and hostname spreading limits
single-node concentration. Enabled role PDBs allow unhealthy-pod eviction so a
PDB does not trap a broken replica.

Before SIGTERM, kubelet executes the configured Sleep lifecycle action (10
seconds by default) so endpoint and load-balancer removal can propagate. The
remaining termination grace period belongs to ASP.NET/MassTransit draining; the
chart rejects a sleep that consumes the whole grace period. Capacity approval
still requires a real rollout/SIGTERM drill proving requests stop, consumers
finish or safely redeliver in-flight messages, and jobs release leases before
the grace period expires. Canary traffic shifting and automatic SLO rollback
remain responsibilities of the selected edge/GitOps controller.

## External dependency contract

The chart permits dependencies; it does not provision them. A production
overlay must bind these contracts to reviewed provider IaC:

- MariaDB exposes a multi-zone HA writer endpoint over verified TLS. Runtime
  Secrets use separate DML-only identities and bounded pools; migration alone
  uses DDL. Automated backups, PITR retention, restore drills, replica lag,
  storage, connection, and failover alarms remain provider evidence.
- RabbitMQ uses a multi-zone cluster, TLS listener, per-service virtual hosts and
  least-privilege users. Durable exchanges/queues, quorum queues, publisher
  confirms, dead-letter/delayed-redelivery prerequisites, queue depth, unacked,
  disk/memory, partition, and failover monitoring must be verified against the
  selected broker/operator.
- Redis is cache-only, uses a TLS HA endpoint and least-privilege ACL, has an
  approved bounded memory/eviction policy, and never becomes authoritative.
  The shared cache fails open to L1 or the bounded database source; provider
  failover, memory pressure, evictions, latency, and degraded-health alarms need
  external evidence.

## Validation

The committed CI values use RFC 5737 documentation addresses and a zero digest;
they are render-only and cannot pull a production image.

```sh
helm lint DevOps/charts/inspacy-service \
  --kube-version 1.31.0 \
  --values DevOps/charts/inspacy-service/ci/values.yaml
helm template comment DevOps/charts/inspacy-service \
  --namespace inspacy-production \
  --kube-version 1.31.0 \
  --values DevOps/charts/inspacy-service/ci/values.yaml
```

Before promotion, render the real overlay, scan it, verify its signatures and
provenance, and attach the output plus rollout/fault evidence to the controlled
external release record.
