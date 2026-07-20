# Production environment contract

The files in this directory are render-only shapes, not deployable production
configuration. They deliberately use `registry.invalid`, a zero image digest,
RFC 5737 documentation CIDRs, placeholder Secret names, and example workload
identity annotations. A protected overlay must replace every one of those values
and pass admission, connectivity, rollout, and fault validation before approval.

Render one service by applying the base first and its service profile second:

```sh
helm lint DevOps/charts/inspacy-service --strict --kube-version 1.31.0 \
  -f DevOps/environments/production/base.values.example.yaml \
  -f DevOps/environments/production/feed.values.example.yaml
helm template feed DevOps/charts/inspacy-service \
  --namespace inspacy-production --kube-version 1.31.0 \
  -f DevOps/environments/production/base.values.example.yaml \
  -f DevOps/environments/production/feed.values.example.yaml
```

## Infrastructure contract

The real environment must provide reviewed IaC and evidence for:

- a multi-zone MariaDB writer endpoint with certificate/hostname verification,
  isolated per-role DML users, a migration-only DDL user, bounded connections,
  automated backups and PITR, restore/failover drills, and lag/storage/connection
  alarms;
- a multi-zone RabbitMQ cluster with TLS on 5671, per-service virtual hosts and
  ACLs, durable topology and quorum queues, publisher confirms, dead-letter and
  delayed-redelivery prerequisites, and partition/disk/memory/queue/unacked
  alarms;
- a TLS Redis HA endpoint with per-service ACLs, bounded memory and an approved
  eviction policy for disposable cache data. Feed's API Secret and each
  Interaction runtime Secret supply `Cache__Redis__ConnectionString` with TLS
  and hostname verification. Redis loss must remain fail-open to L1 or bounded
  authoritative reads, and the failover/memory/eviction/latency alarms must be
  exercised;
- three or more failure domains labelled `topology.kubernetes.io/zone`, an
  ingress/monitoring labelling contract matching both namespace and pod
  selectors, a CNI that enforces ingress and egress NetworkPolicy, a metrics
  adapter for every HPA signal, and reviewed image signature/provenance policy.

Runtime API, consumer, and job workloads and the migration Job must retain
separate ServiceAccounts, workload identities, and Secrets. Runtime users cannot
perform DDL; migration cannot fetch runtime identity, broker, cache, signing, or
storage secrets. Keep pod and container security contexts, resource requests and
limits, topology constraints, PDBs, finite HPA/KEDA maxima, and autoscaler
stabilization windows in the protected overlay. If KEDA owns a Deployment,
disable the chart HPA for that role and review the external metric failure mode.

## Evidence that cannot be committed here

Repository lint and rendering prove only manifest shape. Phase 6 remains open
until the target environment supplies:

- provider plan/apply review, HA topology and TLS/ACL inspection output;
- backup/PITR restore evidence with measured RPO/RTO;
- MariaDB, RabbitMQ, Redis, node, and zone failover results;
- positive and negative NetworkPolicy connectivity tests after admission;
- image signature/SBOM/provenance verification and runtime security inspection;
- API rollout and SIGTERM drain results, consumer redelivery/no-loss results,
  lease release, PDB/node-drain behavior, and zone-loss capacity;
- load/soak/fault data used to approve resource budgets and HPA/KEDA thresholds,
  plus canary rollback and paging evidence.

Store dated outputs under the controlled evidence location referenced by the
implementation plan; never replace real evidence with these examples.
