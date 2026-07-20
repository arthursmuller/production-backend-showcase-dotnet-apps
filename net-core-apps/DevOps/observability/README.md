# Inspacy observability bundle

**Status:** Deployable engineering baseline; collector wiring, production data, paging integration, and drill evidence are release gates

**Owners:** Platform/SRE owns collection, Prometheus, Grafana, and paging. Each service owner owns its application instruments and response. Backend Architecture owns the target contract.

**Approval/evidence due:** 2026-08-14, matching `DevOps/operational-baselines/slo.yaml`

This directory contains provider-neutral Prometheus recording/alert rules and one Grafana dashboard. It does not claim that a production environment exists or meets an objective.

## Contents and installation

- `prometheus/recording-rules.yaml` derives bounded rates, ratios, latency quantiles, and SLO burn from application metrics.
- `prometheus/alert-rules.yaml` pages or tickets owned thresholds and links every alert to a repository runbook.
- `grafana/inspacy-operations.json` is a compact dashboard for API, cache, messaging, Feed, Interaction, and Moderation signals.

Load both YAML files through Prometheus `rule_files`, or through the equivalent rule API of a Prometheus-compatible deployment. Import/provision the JSON dashboard and bind its `DS_PROMETHEUS` variable to the Prometheus data source. Replace repository-relative `runbook` annotations with immutable HTTPS links during deployment so responders can open the exact released revision.

The applications export OTLP only when `OpenTelemetry:OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The release environment therefore needs an OpenTelemetry Collector, or an equivalent OTLP metrics receiver, in the application failure domain. Its Prometheus endpoint and rule evaluation must not depend on an application pod remaining healthy.

## OTLP-to-Prometheus normalization contract

Rules intentionally reference only instruments emitted by repository source, plus recording rules defined in this bundle. The collector must apply the following deterministic translation before these files are enabled:

1. Replace dots in metric and attribute names with underscores.
2. Add `_total` to counters; add `_bucket`, `_sum`, and `_count` to histograms.
3. Translate UCUM `s` to `_seconds`, `ms` to `_milliseconds`, and `By` to `_bytes`. Annotation units such as `{request}` and MassTransit's `ea` must not add another semantic word.
4. Promote `service.name` and `deployment.environment.name` resource attributes to the `service_name` and `deployment_environment_name` labels.
5. Preserve histogram `le` and the bounded source attributes listed below.

This is the standard snake-case/suffix shape expected from a Prometheus exporter, but exporter defaults can change. Platform/SRE must compare the release collector's `/metrics` names to this table and run `promtool check rules` plus representative queries before promotion.

| Source instrument | Expected Prometheus family | Required labels used here |
|---|---|---|
| `http.server.request.duration` (`s`) | `http_server_request_duration_seconds` | `service_name`, `deployment_environment_name`, `http_route`, `http_response_status_code` |
| `inspacy.http.server.request.outcomes` | `inspacy_http_server_request_outcomes_total` | resource labels, route template `http_route`, and exactly one of the 13 fixed `http_request_outcome` values documented below |
| `inspacy.cache.requests`, `.hits`, `.misses`, `.errors`, `.source.rejections` | `inspacy_cache_*_total` | resource labels; errors also `cache_layer`, `cache_operation` |
| `inspacy.cache.operation.duration` (`ms`) | `inspacy_cache_operation_duration_milliseconds` | resource labels, `cache_layer`, `cache_operation` |
| `messaging.masstransit.consume`, `.consume.errors`, `.consume.retries` | `messaging_masstransit_consume*_total` | resource labels, `messaging_masstransit_destination` |
| `messaging.masstransit.consume.duration` (`ms`) | `messaging_masstransit_consume_duration_milliseconds` | resource labels, destination |
| `messaging.masstransit.outbox.delivery.errors`, `.outbox.send.errors` | `messaging_masstransit_outbox_*_errors_total` | resource labels |
| `messaging.masstransit.consume.active` | `messaging_masstransit_consume_active_total` | resource labels, destination |
| `inspacy.messaging.outbox.pending`, `.oldest_age` (`s`), `.sample_success` | `inspacy_messaging_outbox_pending`, `_oldest_age_seconds`, `_sample_success` | resource labels and build-time-bounded `outbox_store`; the primary-key-ordered sampler reads at most the configured readiness bound plus one sentinel, so pending is exact through the bound and a capped lower bound after breach; it never exports payloads, SQL, identifiers, destinations, connection data, or exception text |
| `inspacy.mariadb.command.executions`, `.slow_executions` | `inspacy_mariadb_command_*_total` | resource labels, `db_operation_name`, `inspacy_db_command_outcome`, `inspacy_db_command_slow`, `inspacy_db_command_failure_kind`; failure kind is exactly `none`, `deadlock`, `lock_timeout`, `transient`, or `non_transient` |
| `inspacy.mariadb.command.duration` (`ms`) | `inspacy_mariadb_command_duration_milliseconds` | resource labels and the same bounded database labels; SQL, parameters, resource names, identifiers, and exception details are forbidden |
| `inspacy.feed.audience_projection.outcomes` | `inspacy_feed_audience_projection_outcomes_total` | `relationship_kind`, `projection_outcome` |
| `inspacy.feed.outcomes`, `.batch.size` | `inspacy_feed_outcomes_total`, `inspacy_feed_batch_size` | `operation`, `outcome`, `mode` as applicable |
| `inspacy.feed.activity_projection.convergence.duration` (`s`), `.breaches` | `inspacy_feed_activity_projection_convergence_duration_seconds`, `_breaches_total` | resource labels only; source-to-committed/queryable latency with an exact 60-second breach counter |
| `inspacy.feed.audience_projection.convergence.duration` (`s`), `.breaches` | `inspacy_feed_audience_projection_convergence_duration_seconds`, `_breaches_total` | bounded `projection_source` (`event`, `snapshot`, `unknown`) |
| `inspacy.feed.revocation.completion.duration` (`s`), `.breaches` | `inspacy_feed_revocation_completion_duration_seconds`, `_breaches_total` | bounded `revocation_kind`; source-to-committed-final-sweep latency with an exact five-second breach counter |
| `inspacy.interaction.projection.gaps` | `inspacy_interaction_projection_gaps_total` | `projection` |
| `inspacy.interaction.ingestion.outcomes` | `inspacy_interaction_ingestion_outcomes_total` | fixed `ingestion_kind` and `ingestion_outcome` (`applied`, `deduplicated`, `ignored`, `gap_buffered`); never event/resource IDs |
| `inspacy.interaction.reconciliation.drift` | `inspacy_interaction_reconciliation_drift` histogram | fixed `counter` (`likes`, `dislikes`, `rating_count`, `rating_total`); absolute aggregate difference only |
| `inspacy.interaction.projection.gap.unresolved`, `.oldest_age` (`s`), `.sample_timestamp` (`s`) | `inspacy_interaction_projection_gap_unresolved`, `_oldest_age_seconds`, `_sample_timestamp_seconds` | resource labels only; aggregate count/time, never resource identifiers |
| `moderation.reports.received`, `.reports.duplicate`, `.decisions`, `.cases.escalated` | `moderation_*_total` | resource labels; decisions also bounded `action` |
| `moderation.workflow.rejections`, `.operation.boundary_cancellations` | `moderation_workflow_rejections_total`, `moderation_operation_boundary_cancellations_total` | fixed `reason` (`version_conflict`, `workflow_conflict`) or `boundary` (`application_mutation`); never cases, reports, actors, or evidence |
| `moderation.operation.duration` (`ms`) | `moderation_operation_duration_milliseconds` | resource labels |
| `moderation.first_response.duration` (`s`), `.breaches` | `moderation_first_response_duration_seconds`, `moderation_first_response_breaches_total` | resource labels only; elapsed aggregate observation, never case/report identifiers |
| `moderation.sla.active`, `.overdue`, `.oldest_overdue_age` (`s`), `.sample_timestamp` (`s`) | `moderation_sla_active`, `_overdue`, `_oldest_overdue_age_seconds`, `_sample_timestamp_seconds` | resource labels only; restricted aggregate counts/times |

MassTransit 8.5.10's exact instrument and label names come from its configured `MassTransit` meter. Its active-work instruments are implemented as `Counter<long>` with positive and negative updates, not `UpDownCounter<long>`. The dashboard labels that series diagnostic-only and no alert depends on it until the release collector proves correct current-value semantics.

The generic consume filter contributes a structured logging scope containing `TraceId`, `SpanId`, `CorrelationId`, `MessageId`, and the build-time contract type. `UseInstrumentation` remains the sole propagation mechanism. The filter never logs the message payload, and none of those identifiers become custom metric labels.

The HTTP outcome set is exactly `success`, `invalid_request`, `authentication_required`,
`access_forbidden`, `not_found`, `conflict`, `business_rejection`, `client_rate_limited`,
`load_shed`, `server_timeout`, `dependency_unavailable`, `server_error`, and
`client_cancelled`. The availability denominator contains `success` plus the four eligible
failure outcomes: `load_shed`, `server_timeout`, `dependency_unavailable`, and `server_error`.
Authentication, authorization, malformed input, missing resources, conflicts, business rules,
caller quotas, and caller cancellation remain queryable but are excluded from the SLO. Global
replica concurrency rejection is explicitly tagged and returned as retryable 503 load shedding;
the bounded per-identity write quota remains a 429 client-rate-limit outcome.

## Threshold ownership

| Signal | Threshold in this bundle | Source/owner |
|---|---|---|
| Fast availability burn | >14.4x over 1h for 5m | `DevOps/operational-baselines/slo.yaml`; Platform/SRE |
| Slow availability burn | >2x over 6h for 15m | `DevOps/operational-baselines/slo.yaml`; Platform/SRE |
| API p95 | service-specific 150-400 ms for 15m | `DevOps/operational-baselines/slo.yaml`; service owner |
| Cache error ratio | >1% with >1 request/s for 10m | initial operational guard; service owner must tune from representative evidence |
| Cache source rejection | >0.1/s for 10m | dependency-degradation runbook; Platform/SRE |
| Consumer faults | >0.1% and at least 5/5m, sustained 5m | service owner; tune per contract after soak evidence |
| Any MassTransit outbox fault | >0 for 1m | service owner/Platform/SRE |
| EF bus-outbox oldest age | >30s for 2m | service owner/Platform/SRE; source objective is `DevOps/operational-baselines/slo.yaml` |
| EF bus-outbox aggregate sampler | latest sample unsuccessful for 2m | service owner/Data Platform |
| MariaDB slow-command ratio | >1% with >1 command/s for 10m; slow means the required per-service setting, currently 250 ms | Data Platform/service owner; tune only from representative evidence |
| MariaDB command failures | >0.1/s for 5m | Data Platform/service owner |
| MariaDB deadlocks | typed error 1213 continuously observed for 5m | Data Platform/service owner |
| Block projection quarantine | any observed event | Feed/Security |
| Feed activity/audience convergence | any measured completion >60s | Feed/Security; `DevOps/operational-baselines/slo.yaml` |
| Feed visibility revocation completion | any measured final sweep >5s | Feed/Security; `DevOps/operational-baselines/slo.yaml` |
| Other Feed/Interaction gap or conflict outcome | any sustained outcome | Feed or Interaction owner |
| Interaction unresolved projection gap | oldest age >60s for 2m, or aggregate sample older than 120s for 2m | Interaction owner; `DevOps/operational-baselines/slo.yaml` |
| Interaction reconciliation drift | any repeated drift observation for 10m | Interaction owner |
| Feed pull publisher cap | any cap event; cap is 64 | `DevOps/operational-baselines/capacity-envelope.yaml`; Feed owner |
| Moderation SLA escalation | any | Moderation owner; escalation is a missed configured case deadline |
| Moderation first response | any response >14,400s; any active overdue case for 2m; aggregate sample older than 120s for 2m | Moderation/Trust & Safety; `DevOps/operational-baselines/slo.yaml` |
| Moderation report-intake p95 | >400 ms for 15m | `DevOps/operational-baselines/slo.yaml`; Moderation owner |
| Moderation workflow rejection or mutation cancellation | >0.1/s for 10m | Moderation owner; tune from representative workflow evidence |

The cache and consumer thresholds are safe starting guards, not measured limits. Their owner must attach representative baseline, false-positive review, and any approved adjustment to the release record.

## Cardinality contract

Allowed labels are environment, service, runtime role when it becomes available, HTTP method/status/route template, the fixed HTTP request-outcome set, cache layer/operation, MassTransit destination/contract/consumer/exception class, the build-time-bounded outbox store type, the bounded MariaDB system/operation/outcome/slow/failure-kind set, Feed operation/outcome/mode, relationship kind/projection outcome, bounded Feed projection source/revocation kind, Interaction projection/ingestion kind/ingestion outcome/counter, and Moderation action/rejection reason/operation boundary. Route values must be endpoint templates, never raw paths.

User, tenant, resource, comment/post, case, report, message, correlation, trace/span, request, IP, URL/query, cache key, evidence, filename, exception message, and arbitrary error text are forbidden metric labels. They belong in access-controlled traces or redacted logs when necessary. Platform/SRE must enforce a series budget per service and reject a collector change that expands a bounded label from code without review.

## Explicit integration gates

The following release signals are absent from the configured meters. They are not represented by invented Prometheus names:

| Missing queryable signal | Required integration and acceptance gate | Owner |
|---|---|---|
| Runtime role per process | add a bounded resource attribute matching API/consumer/job/migration roles; required for per-role capacity panels | API Platform |
| RabbitMQ queue depth/oldest age, unroutable/error queues, node memory/disk/quorum alarms | management/Prometheus plugin collector with stable queue-owner labels; prove <30s healthy age and disk <60% | Messaging Platform/SRE |
| MariaDB active/max/pending connections, pool wait, CPU, storage, and replica lag | MySqlConnector/managed-database collectors selected and allowlisted; correlate them with the implemented safe application command-duration/slow/failure metrics and prove pool wait <=50ms, connection use <75%, p95 CPU <65%, storage <70% | Data Platform/SRE |
| MariaDB execution-strategy retry attempts | the command hook classifies typed transient/deadlock failures but cannot prove whether EF retried them; add and validate a supported EF execution-strategy observer before querying retry counts | Data Platform |
| MassTransit EF inbox duplicate suppressions and normalized consumer cancellations | suppressed inbox deliveries do not enter public consume filters/observers, and cancellation faults do not retain a stable distinguishable shape in the current harness; require a supported library instrument/event and executable harness proof before adding a metric | Messaging Platform |
| Redis memory, eviction, replication/failover state | managed/cache collector; prove memory <65% and no unsafe eviction mode | Cache Platform/SRE |

No launch approval may infer these missing signals from HTTP success or from the existence of this dashboard.

## Release validation checklist

- [ ] YAML parses and `promtool check rules` passes against the exact Prometheus version.
- [ ] Every expected raw metric is present for each applicable runtime role; missing role-specific meters are recorded, not silently ignored.
- [ ] A controlled request, cache miss/error, consumer success/fault, outbox delivery/fault, projection gap, and Moderation escalation exercises the expected recording rule and alert.
- [ ] Alert labels route to the named owner and immutable runbook URL; page/ticket severities reach the correct non-production sink during the drill.
- [ ] Cardinality and ingestion volume stay within the approved budget during representative load and soak.
- [ ] Dashboard panels show no unexplained empty results, counter resets, duplicate collector series, or label collisions.
- [ ] Evidence links the application image, collector, Prometheus rules, dashboard revision, and drill timeline in the controlled external release record.

Repository parse/validation results belong in the change report; production SLO evidence belongs in the restricted operational evidence system.
