# Domain performance runners

The Comment, Feed, Following, Interaction, and Moderation load, stress, and
soak projects are standalone black-box executables. They send no traffic unless
`--execute` (or `PERF_EXECUTE=true`) and a bounded run ID are supplied. Loopback
is the only allowed target by default. A non-loopback target additionally
requires `PERF_ALLOW_NON_LOOPBACK=true` and a non-production
`PERF_TARGET_ENVIRONMENT`; `prod` and `production` are always refused.

Example local smoke run from `net-core-apps`:

```powershell
dotnet run --project Services/Comment/Comment.Tests/Comment.LoadTests -- `
  --execute --run-id local-comment-smoke --profile smoke
```

## Operation mixes

Each `profiles.json` owns a bounded operation catalog and a weighted mix per
profile. Names are ASCII and limited to 64 characters; a catalog and mix contain
at most 32 entries; paths are application-relative; only GET, HEAD, POST, PUT,
PATCH, and DELETE are accepted; and request bodies are limited to 256 KiB of
UTF-8. GET and HEAD bodies are refused. JSON bodies are parsed after substituting
the deterministic `{runId}`, `{sequence}`, and `{sequence1}` placeholders.

Selection is deterministic: the global request sequence is reduced modulo the
sum of weights and matched against the mix in declared order. A complete weight
cycle therefore produces the exact declared ratio. Profile validation rejects
unknown or duplicate operations and invalid weights before traffic starts.

The committed smoke mixes contain at least two anonymous, read-only operations.
The disabled baseline and representative templates use real domain routes and
the engineering ratios in `DevOps/operational-baselines/workload-envelope.yaml` where that file
defines a ratio. They deliberately contain no approved duration, concurrency,
RPS, timeout, credential, or production endpoint. Templates that include a
write require both `PERF_ALLOW_MUTATIONS=true` and, when declared authenticated,
`PERF_ACCESS_TOKEN`. A bearer token is sent only to operations whose catalog
entry sets `requiresAuthentication: true`. Mutations also receive a deterministic
`Idempotency-Key`; neither it nor request paths/bodies/tokens enter an artifact.

Setting any of `PERF_PATH`, `PERF_METHOD`, `PERF_REQUEST_BODY`,
`PERF_CONTENT_TYPE`, or `PERF_REQUIRES_AUTHENTICATION` creates a single explicit
override operation for route-focused diagnosis. It still receives all target,
method, mutation, authentication, and size checks.

## Distinct arrival models

- Load uses `paced-fixed-rate`: bounded workers pace toward one fixed aggregate
  target for the configured duration.
- Stress uses `linear-concurrency-ramp-with-recovery`: workers activate from
  `startConcurrency` to the maximum over the run, then return to the initial
  worker count and initial aggregate target during a required recovery window.
- Soak uses `periodic-burst`: bounded workers hold the base target, raise it by a
  declared multiplier at the end of every period, then return to base. The
  disabled template records the workload model's five-minute hourly 3x burst,
  but leaves total duration and RPS unapproved.

These are bounded, response-aware injectors: target RPS is a schedule, not a
claim that the injector or service achieved it. Representative evidence must
also prove injector saturation was acceptable.

`baseline` and `representative` remain disabled. Running either requires
explicit positive values for `PERF_DURATION_SECONDS`, `PERF_CONCURRENCY`,
`PERF_TARGET_RPS`, and `PERF_REQUEST_TIMEOUT_SECONDS`. Stress additionally needs
a positive `PERF_RECOVERY_SECONDS` and a start concurrency below the maximum.

## Configuration and artifacts

Common configuration:

- `PERF_BASE_URL`, `PERF_OUTPUT_DIR`, and the route override variables select
  the target, output directory, and optional one-route diagnostic.
- `PERF_ACCESS_TOKEN` is accepted only from the environment and is never
  serialized. No profile contains a credential field.
- `PERF_BURST_EVERY_SECONDS`, `PERF_BURST_DURATION_SECONDS`, and
  `PERF_BURST_MULTIPLIER` can override a soak schedule.
- `PERF_MAX_ERROR_RATE_PERCENT`, `PERF_MAX_P95_MS`,
  `PERF_MIN_THROUGHPUT_RPS`, `PERF_MAX_FRESHNESS_LAG_MS`, and
  `PERF_MAX_BACKLOG_COUNT` define optional gates.
- `PERF_DEPENDENCY_SATURATION_PERCENT`, `PERF_TARGET_CPU_PERCENT`,
  `PERF_TARGET_WORKING_SET_MB`, `PERF_FRESHNESS_LAG_MS`, and
  `PERF_BACKLOG_COUNT` attach externally observed resource/freshness data.

Every completed run writes a canonical JSON artifact conforming to
`performance-result.schema.json` and a Markdown summary. Both contain the exact
arrival model plus total and per-operation request/success/error counts and
p50/p95/p99. Operation output is limited to its bounded name, method, configured
weight, and measurements. Paths, bodies, bearer tokens, and user or moderation
content are excluded by construction.

Smoke results validate only the harness and local route wiring. They are not a
production capacity baseline or representative evidence.

## Representative exercise evidence

Performance output is one input to the broader recovery and capacity gates. The
repository also provides a canonical 12-exercise plan, strict plan/result JSON schemas,
and a validator/scaffolder for rebuild, migration, failover, restore,
cache-zero, backlog, connection, cost, and canary evidence:

```powershell
python Tests/PerformanceHelpers/representative_evidence.py validate-plan
python Tests/PerformanceHelpers/representative_evidence.py self-test
```

CI runs those commands with CPython 3.13.14 in a disposable virtual environment.
`requirements.lock.txt` pins `jsonschema` and its complete Python 3.13 dependency
closure with exact wheel hashes. The lock deliberately targets the Linux x64 CI
runner because `rpds-py` is a native wheel; CI installs binary wheels only, with
pip caching disabled and `--require-hashes` enabled.

From `net-core-apps`, the equivalent locked install is:

```powershell
python -m venv <temporary-venv>
<temporary-venv-python> -m pip install --no-cache-dir --only-binary=:all: --require-hashes --requirement Tests/PerformanceHelpers/requirements.lock.txt
```

Changing Python, platform, `jsonschema`, or any transitive version requires a
reviewed lock regeneration and a clean install plus both validator commands.
Do not add an unhashed fallback, source-build fallback, or optional format extra
to make a failed locked restore pass.

See the workspace `README.md` operations sections. The scaffolder accepts
only named non-production environments and writes only to an existing location
outside the repository. It never runs provider mutations or accepts credentials;
those remain in reviewed restricted operational automation. A terminal result
cannot validate as passed until all planned phases, faults, metrics, immutable
artifacts, retention, safety attestations, and approvals are complete.

Approval names in `reviewers` are informational and never satisfy a gate. Each
exercise declares exact approver roles. Every terminal approval is a typed record
bound to the run's source revision, complete workload digest set, infrastructure
revision, and an immutable `approval-record` artifact; the disposition references
those record IDs.

`ddl-role-separation` still executes only on a disposable non-production schema.
Passing additionally requires exactly one independently bound, non-secret production
principal/grant attestation for each of Comment, Feed, Following, Interaction, and
Moderation. Each binds the exact deployed identity revision, secret-revision hash,
workload digest, production-environment revision, and grant-policy digest. Its
immutable evidence includes a domain-specific production grant/policy export and
claims for denied runtime DDL, successful runtime DML smoke, migration-only DDL, and
cross-role isolation. Database Engineering, Security, and the service owner must each
approve the run and all five imported attestations.

`cache-zero-backlog` cannot pass on healthy-resource observations alone. The plan
requires a `source-capacity-exhaustion` fault, at least one observed controlled shed
response, and zero admissions above the declared bounded source capacity.
