#!/usr/bin/env python3
"""Validate and scaffold sanitized representative-exercise evidence.

This tool deliberately does not execute provider mutations. Infrastructure fault,
restore, failover, or traffic commands stay in reviewed operational automation.
The repository tool enforces the common non-production safety and evidence contract.
"""

from __future__ import annotations

import argparse
import copy
import json
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Iterable

try:
    from jsonschema import Draft202012Validator, FormatChecker
except ImportError as error:  # pragma: no cover - environment-dependent message
    raise SystemExit(
        "representative_evidence.py requires the Python 'jsonschema' package (major version 4)."
    ) from error


HELPER_DIRECTORY = Path(__file__).resolve().parent
NET_CORE_ROOT = HELPER_DIRECTORY.parents[1]
DEFAULT_PLAN = HELPER_DIRECTORY / "representative-exercise-plan.json"
PLAN_SCHEMA = HELPER_DIRECTORY / "representative-exercise-plan.schema.json"
RESULT_SCHEMA = HELPER_DIRECTORY / "representative-exercise-result.schema.json"

EXERCISE_TYPES = {
    "projection-rebuild",
    "performance-baseline",
    "ddl-role-separation",
    "migration-recovery",
    "storage-scale-read-replica",
    "dependency-resilience",
    "database-failover",
    "backup-restore-pitr",
    "az-region-failback",
    "cache-zero-backlog",
    "connection-cost-headroom",
    "canary-rollback",
}
EXPECTED_GATES = {"0.7", "0.10", "2.9", "2.10", "2.11", "6.9", "6.10", "6.11", "6.12"}
EXPECTED_APPROVER_ROLES = {
    "projection-rebuild": {"service-owner", "platform-sre", "security"},
    "performance-baseline": {"service-owner", "platform-sre", "product"},
    "ddl-role-separation": {"database-engineering", "security", "service-owner"},
    "migration-recovery": {"database-engineering", "service-owner", "release-manager"},
    "storage-scale-read-replica": {"database-engineering", "service-owner", "platform-sre"},
    "dependency-resilience": {
        "platform-sre",
        "service-owner",
        "messaging-owner",
        "cache-owner",
    },
    "database-failover": {"database-engineering", "service-owner", "platform-sre"},
    "backup-restore-pitr": {"database-engineering", "security", "service-owner"},
    "az-region-failback": {"platform-sre", "security", "service-owner", "product"},
    "cache-zero-backlog": {
        "cache-owner",
        "database-engineering",
        "service-owner",
        "platform-sre",
    },
    "connection-cost-headroom": {
        "platform-sre",
        "database-engineering",
        "service-owner",
        "finance-cost",
    },
    "canary-rollback": {"release-manager", "platform-sre", "service-owner"},
}
DDL_APPROVER_ROLES = {"database-engineering", "security", "service-owner"}
DDL_SERVICE_DOMAINS = {"comment", "feed", "following", "interaction", "moderation"}
DDL_ATTESTATION_CLAIMS = {
    "runtime-ddl-denied",
    "runtime-dml-smoke-passed",
    "migration-only-ddl-verified",
    "cross-role-isolation-verified",
}
ATTESTATION_CLAIM_FIELDS = {
    "runtime-ddl-denied": "runtimeDdlDenied",
    "runtime-dml-smoke-passed": "runtimeDmlSmokePassed",
    "migration-only-ddl-verified": "migrationOnlyDdlVerified",
    "cross-role-isolation-verified": "crossRoleIsolationVerified",
}
ATTESTATION_CLAIM_ARTIFACT_KINDS = {
    "runtimeDdlDenied": "command-log",
    "runtimeDmlSmokePassed": "command-log",
    "migrationOnlyDdlVerified": "grant-report",
    "crossRoleIsolationVerified": "grant-report",
}
DDL_REQUIRED_ARTIFACTS = {
    "approval-record",
    "command-log",
    "grant-report",
    "production-grant-policy-export",
    "production-principal-attestation",
}
CACHE_SHEDDING_METRICS = {
    "cachezero.shed-responses-observed": ("count", ">=", 1),
    "cachezero.admissions-over-bounded-capacity": ("count", "==", 0),
}
SAFE_ID = re.compile(r"^[A-Za-z0-9._-]+$")
SOURCE_REVISION = re.compile(r"^[0-9a-fA-F]{7,64}$")
IMAGE_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
SECRET_VALUE = re.compile(
    r"(?i)(password\s*=|authorization\s*[:=]\s*bearer|bearer\s+[A-Za-z0-9._-]{12,}|client_secret\s*=)"
)
SENSITIVE_KEY_PARTS = ("password", "access_token", "client_secret", "connectionstring")
SENSITIVE_KEY_ALLOWLIST = {"credentialsFromEnvironment"}


class EvidenceError(ValueError):
    """A deterministic validation failure suitable for operator output."""


def _duplicate_safe_object(pairs: Iterable[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise EvidenceError(f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def load_json(path: Path) -> Any:
    try:
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_duplicate_safe_object,
        )
    except FileNotFoundError as error:
        raise EvidenceError(f"JSON file does not exist: {path}") from error
    except json.JSONDecodeError as error:
        raise EvidenceError(f"Invalid JSON in {path}: {error}") from error


def validate_json_schema(document: Any, schema_path: Path) -> None:
    schema = load_json(schema_path)
    Draft202012Validator.check_schema(schema)
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    failures = sorted(validator.iter_errors(document), key=lambda item: list(item.absolute_path))
    if failures:
        rendered = []
        for failure in failures[:20]:
            location = "/".join(str(part) for part in failure.absolute_path) or "$"
            rendered.append(f"{location}: {failure.message}")
        raise EvidenceError("Schema validation failed:\n  " + "\n  ".join(rendered))


def _assert_unique(values: list[Any], label: str) -> None:
    if len(values) != len(set(values)):
        raise EvidenceError(f"{label} contains duplicate values.")


def _scan_for_secrets(value: Any, path: str = "$") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = key.lower()
            if key not in SENSITIVE_KEY_ALLOWLIST and any(
                part in normalized for part in SENSITIVE_KEY_PARTS
            ):
                raise EvidenceError(f"Sensitive property name is forbidden at {path}/{key}.")
            _scan_for_secrets(child, f"{path}/{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _scan_for_secrets(child, f"{path}/{index}")
    elif isinstance(value, str):
        if SECRET_VALUE.search(value):
            raise EvidenceError(f"Potential credential material is forbidden at {path}.")


def validate_plan(document: dict[str, Any]) -> dict[str, Any]:
    validate_json_schema(document, PLAN_SCHEMA)
    _scan_for_secrets(document)

    allowed = {item.lower() for item in document["allowedTargetEnvironments"]}
    forbidden = {item.lower() for item in document["forbiddenTargetEnvironments"]}
    if not {"prod", "production"}.issubset(forbidden):
        raise EvidenceError("The plan must explicitly forbid prod and production.")
    if allowed & forbidden:
        raise EvidenceError("Allowed and forbidden target environments overlap.")

    exercises = document["exercises"]
    types = [exercise["type"] for exercise in exercises]
    _assert_unique(types, "Exercise types")
    if set(types) != EXERCISE_TYPES:
        missing = sorted(EXERCISE_TYPES - set(types))
        extra = sorted(set(types) - EXERCISE_TYPES)
        raise EvidenceError(f"Exercise coverage mismatch; missing={missing}, extra={extra}.")

    covered_gates: set[str] = set()
    for exercise in exercises:
        exercise_type = exercise["type"]
        covered_gates.update(exercise["gates"])
        _assert_unique(exercise["requiredPhases"], f"{exercise_type} phases")
        _assert_unique(exercise["requiredFaults"], f"{exercise_type} faults")
        _assert_unique(exercise["requiredApproverRoles"], f"{exercise_type} approver roles")
        if set(exercise["requiredApproverRoles"]) != EXPECTED_APPROVER_ROLES[exercise_type]:
            raise EvidenceError(
                f"{exercise_type} approver roles do not match the repository contract."
            )
        metric_names = [metric["name"] for metric in exercise["requiredMetrics"]]
        _assert_unique(metric_names, f"{exercise_type} metrics")
        attestation_types = [item["type"] for item in exercise["requiredAttestations"]]
        _assert_unique(attestation_types, f"{exercise_type} attestation types")
        for requirement in exercise["requiredAttestations"]:
            _assert_unique(
                requirement["requiredServiceDomains"],
                f"{exercise_type} attestation service domains",
            )
            _assert_unique(
                requirement["requiredClaims"],
                f"{exercise_type} attestation claims",
            )
            _assert_unique(
                requirement["requiredApprovalRoles"],
                f"{exercise_type} attestation approval roles",
            )

        if exercise_type == "ddl-role-separation":
            requirements = exercise["requiredAttestations"]
            if len(requirements) != 1:
                raise EvidenceError(
                    "ddl-role-separation requires exactly one production principal/grant "
                    "attestation contract."
                )
            requirement = requirements[0]
            if (
                requirement["type"] != "production-principal-grant"
                or set(requirement["requiredServiceDomains"]) != DDL_SERVICE_DOMAINS
                or set(requirement["requiredClaims"]) != DDL_ATTESTATION_CLAIMS
                or set(requirement["requiredApprovalRoles"]) != DDL_APPROVER_ROLES
            ):
                raise EvidenceError(
                    "ddl-role-separation production attestation domains, claims, or "
                    "approval roles do not match the repository contract."
                )
            missing_artifacts = DDL_REQUIRED_ARTIFACTS - set(exercise["requiredArtifacts"])
            if missing_artifacts:
                raise EvidenceError(
                    "ddl-role-separation is missing required production evidence artifact "
                    f"kinds: {sorted(missing_artifacts)}"
                )
        elif exercise["requiredAttestations"]:
            raise EvidenceError(
                f"{exercise_type} may not import production-principal attestations."
            )

        if exercise_type == "cache-zero-backlog":
            if "source-capacity-exhaustion" not in exercise["requiredFaults"]:
                raise EvidenceError(
                    "cache-zero-backlog must inject source-capacity-exhaustion."
                )
            metrics_by_name = {item["name"]: item for item in exercise["requiredMetrics"]}
            for name, expected in CACHE_SHEDDING_METRICS.items():
                metric = metrics_by_name.get(name)
                actual = (
                    metric["unit"],
                    metric["operator"],
                    metric["target"],
                ) if metric else None
                if actual != expected:
                    raise EvidenceError(
                        f"cache-zero-backlog overload-shedding metric {name} was removed "
                        "or weakened."
                    )
        runbook_reference = exercise["runbook"]
        runbook_path = runbook_reference.split("#", maxsplit=1)[0]
        runbook = (NET_CORE_ROOT / runbook_path).resolve()
        workspace_readme = (NET_CORE_ROOT.parent / "README.md").resolve()
        if runbook != workspace_readme or not runbook.is_file():
            raise EvidenceError(
                f"{exercise_type} runbook must reference the workspace README: {runbook}"
            )

    if covered_gates != EXPECTED_GATES:
        raise EvidenceError(
            f"Gate coverage mismatch; missing={sorted(EXPECTED_GATES - covered_gates)}, "
            f"extra={sorted(covered_gates - EXPECTED_GATES)}."
        )
    return document


def _exercise(plan: dict[str, Any], exercise_type: str) -> dict[str, Any]:
    return next(item for item in plan["exercises"] if item["type"] == exercise_type)


def _parse_utc(value: str, label: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise EvidenceError(f"{label} is not a valid ISO-8601 timestamp.") from error
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise EvidenceError(f"{label} must include a UTC offset.")
    return parsed.astimezone(timezone.utc)


def _metric_passed(operator: str, actual: float, target: float) -> bool:
    if operator == "<=":
        return actual <= target
    if operator == ">=":
        return actual >= target
    if operator == "==":
        return actual == target
    raise EvidenceError(f"Unsupported metric operator: {operator}")


def _validate_phase_timing(item: dict[str, Any], label: str) -> None:
    status = item["status"]
    started_value = item["startedAtUtc"]
    finished_value = item["finishedAtUtc"]
    if status == "not-started":
        if started_value is not None or finished_value is not None:
            raise EvidenceError(f"{label} is not-started but has timestamps.")
        return
    if started_value is None:
        raise EvidenceError(f"{label} status {status} requires startedAtUtc.")
    started = _parse_utc(started_value, f"{label} startedAtUtc")
    if status == "running":
        if finished_value is not None:
            raise EvidenceError(f"{label} is running but already has finishedAtUtc.")
        return
    if finished_value is None:
        raise EvidenceError(f"{label} status {status} requires finishedAtUtc.")
    finished = _parse_utc(finished_value, f"{label} finishedAtUtc")
    if finished < started:
        raise EvidenceError(f"{label} finishedAtUtc precedes startedAtUtc.")


def _require_artifact_kind(
    artifact_by_id: dict[str, dict[str, Any]],
    artifact_ref: str,
    expected_kind: str,
    label: str,
) -> None:
    artifact = artifact_by_id.get(artifact_ref)
    if artifact is None:
        raise EvidenceError(f"{label} references undeclared artifact {artifact_ref}.")
    if artifact["kind"] != expected_kind:
        raise EvidenceError(
            f"{label} requires artifact kind {expected_kind}, not {artifact['kind']}."
        )


def validate_result(document: dict[str, Any], plan: dict[str, Any]) -> dict[str, Any]:
    validate_json_schema(document, RESULT_SCHEMA)
    _scan_for_secrets(document)

    if document["planId"] != plan["planId"]:
        raise EvidenceError("Result planId does not match the validated plan.")
    exercise = _exercise(plan, document["exerciseType"])
    if set(document["gates"]) != set(exercise["gates"]):
        raise EvidenceError("Result gates do not exactly match the selected exercise.")

    environment = document["targetEnvironment"].lower()
    allowed = {item.lower() for item in plan["allowedTargetEnvironments"]}
    forbidden = {item.lower() for item in plan["forbiddenTargetEnvironments"]}
    if environment in forbidden or environment not in allowed:
        raise EvidenceError(
            f"Target environment '{environment}' is not an allowed representative target."
        )

    started = _parse_utc(document["startedAtUtc"], "startedAtUtc")
    finished_value = document["finishedAtUtc"]
    finished = _parse_utc(finished_value, "finishedAtUtc") if finished_value else None
    if finished and finished < started:
        raise EvidenceError("finishedAtUtc precedes startedAtUtc.")

    phase_names = [item["name"] for item in document["phases"]]
    _assert_unique(phase_names, "Result phases")
    phase_by_name = {item["name"]: item for item in document["phases"]}
    for phase in document["phases"]:
        _validate_phase_timing(phase, f"Phase {phase['name']}")
    missing_phases = set(exercise["requiredPhases"]) - set(phase_by_name)
    if missing_phases:
        raise EvidenceError(f"Result is missing phases: {sorted(missing_phases)}")

    fault_names = [item["name"] for item in document["faults"]]
    _assert_unique(fault_names, "Result faults")
    fault_by_name = {item["name"]: item for item in document["faults"]}
    for fault in document["faults"]:
        _validate_phase_timing(fault, f"Fault {fault['name']}")
    missing_faults = set(exercise["requiredFaults"]) - set(fault_by_name)
    if missing_faults:
        raise EvidenceError(f"Result is missing faults: {sorted(missing_faults)}")

    metric_names = [item["name"] for item in document["metrics"]]
    _assert_unique(metric_names, "Result metrics")
    metric_by_name = {item["name"]: item for item in document["metrics"]}
    for required in exercise["requiredMetrics"]:
        actual_metric = metric_by_name.get(required["name"])
        if actual_metric is None:
            raise EvidenceError(f"Result is missing metric: {required['name']}")
        for field in ("unit", "operator", "target"):
            if actual_metric[field] != required[field]:
                raise EvidenceError(
                    f"Metric {required['name']} changed plan field {field}."
                )
        if actual_metric["actual"] is None:
            if actual_metric["passed"] is not None:
                raise EvidenceError(
                    f"Metric {required['name']} has a pass value without an observation."
                )
        else:
            computed = _metric_passed(
                actual_metric["operator"],
                actual_metric["actual"],
                actual_metric["target"],
            )
            if actual_metric["passed"] is not computed:
                raise EvidenceError(
                    f"Metric {required['name']} passed={actual_metric['passed']} "
                    f"but the declared gate evaluates to {computed}."
                )

    artifact_ids = [item["id"] for item in document["artifacts"]]
    _assert_unique(artifact_ids, "Artifact IDs")
    artifact_by_id = {item["id"]: item for item in document["artifacts"]}
    referenced_artifacts = {
        artifact_id
        for phase in (*document["phases"], *document["faults"])
        for artifact_id in phase["artifactRefs"]
    }
    missing_references = referenced_artifacts - set(artifact_ids)
    if missing_references:
        raise EvidenceError(
            f"Phase/fault artifact references are not declared: {sorted(missing_references)}"
        )
    for artifact in document["artifacts"]:
        if "?" in artifact["uri"] or "#" in artifact["uri"] or "@" in artifact["uri"]:
            raise EvidenceError(
                f"Artifact URI {artifact['id']} may not contain credentials or signed query data."
            )
        retention = _parse_utc(
            artifact["retentionUntilUtc"], f"artifact {artifact['id']} retentionUntilUtc"
        )
        minimum_retention = started + timedelta(days=plan["evidenceRetentionDays"])
        if retention < minimum_retention:
            raise EvidenceError(
                f"Artifact {artifact['id']} retention is shorter than the plan requirement."
            )

    approval_ids = [item["id"] for item in document["approvals"]]
    _assert_unique(approval_ids, "Approval IDs")
    approval_by_id = {item["id"]: item for item in document["approvals"]}
    approval_times: dict[str, datetime] = {}
    for approval in document["approvals"]:
        label = f"Approval {approval['id']}"
        _require_artifact_kind(
            artifact_by_id,
            approval["evidenceRef"],
            "approval-record",
            label,
        )
        if approval["boundSourceRevision"] != document["sourceRevision"]:
            raise EvidenceError(f"{label} is bound to a different source revision.")
        if set(approval["boundImageDigests"]) != set(document["imageDigests"]):
            raise EvidenceError(f"{label} is bound to different workload image digests.")
        if approval["boundInfrastructureRevision"] != document["infrastructureRevision"]:
            raise EvidenceError(f"{label} is bound to a different infrastructure revision.")
        decided_at = _parse_utc(approval["decidedAtUtc"], f"{label} decidedAtUtc")
        if decided_at < started:
            raise EvidenceError(f"{label} precedes the exercise start.")
        approval_times[approval["id"]] = decided_at

    attestation_ids = [item["id"] for item in document["attestations"]]
    _assert_unique(attestation_ids, "Attestation IDs")
    _assert_unique(
        [(item["type"], item["serviceDomain"]) for item in document["attestations"]],
        "Attestation type/service-domain pairs",
    )
    if exercise["type"] != "ddl-role-separation" and document["attestations"]:
        raise EvidenceError(
            "Production-principal attestations are accepted only for ddl-role-separation."
        )
    attestation_import_times: dict[str, datetime] = {}
    for attestation in document["attestations"]:
        label = f"Attestation {attestation['id']}"
        observed = _parse_utc(attestation["observedAtUtc"], f"{label} observedAtUtc")
        imported = _parse_utc(attestation["importedAtUtc"], f"{label} importedAtUtc")
        if imported < observed:
            raise EvidenceError(f"{label} was imported before it was observed.")
        attestation_import_times[attestation["id"]] = imported
        if attestation["workloadRevision"] not in document["imageDigests"]:
            raise EvidenceError(
                f"{label} workload revision is not an exact deployed result image digest."
            )
        _require_artifact_kind(
            artifact_by_id,
            attestation["grantPolicyExportArtifactRef"],
            "production-grant-policy-export",
            label,
        )
        _require_artifact_kind(
            artifact_by_id,
            attestation["attestationArtifactRef"],
            "production-principal-attestation",
            label,
        )
        for claim_field, expected_kind in ATTESTATION_CLAIM_ARTIFACT_KINDS.items():
            _require_artifact_kind(
                artifact_by_id,
                attestation["claimEvidenceRefs"][claim_field],
                expected_kind,
                f"{label} claim {claim_field}",
            )
    if document["attestations"]:
        for field, label in (
            ("principalIdSha256", "production principal hashes"),
            ("deployedIdentityRevision", "deployed identity revisions"),
            ("secretRevisionIdSha256", "secret-revision hashes"),
            ("grantPolicyExportArtifactRef", "grant-policy export references"),
            ("attestationArtifactRef", "attestation artifact references"),
        ):
            _assert_unique(
                [item[field] for item in document["attestations"]],
                f"DDL attestation {label}",
            )
        for claim_field in ATTESTATION_CLAIM_ARTIFACT_KINDS:
            _assert_unique(
                [
                    item["claimEvidenceRefs"][claim_field]
                    for item in document["attestations"]
                ],
                f"DDL attestation {claim_field} evidence references",
            )

    status = document["status"]
    decision = document["disposition"]["decision"]
    disposition_approval_refs = document["disposition"]["approvalRefs"]
    missing_approval_refs = set(disposition_approval_refs) - set(approval_by_id)
    if missing_approval_refs:
        raise EvidenceError(
            "Disposition approval references are not declared: "
            f"{sorted(missing_approval_refs)}"
        )
    if status == "in-progress":
        if (
            finished is not None
            or decision != "pending"
            or document["disposition"]["decidedAtUtc"] is not None
            or disposition_approval_refs
        ):
            raise EvidenceError("An in-progress result must be unfinished with pending disposition.")
        return document

    if finished is None:
        raise EvidenceError("A terminal result requires finishedAtUtc.")
    decided_value = document["disposition"]["decidedAtUtc"]
    if decided_value is None or not disposition_approval_refs:
        raise EvidenceError(
            "A terminal result requires a disposition timestamp and approval evidence references."
        )
    decided = _parse_utc(decided_value, "disposition decidedAtUtc")
    if decided < started:
        raise EvidenceError("Disposition timestamp precedes the exercise start.")
    if finished > decided:
        raise EvidenceError("Disposition timestamp precedes the exercise finish.")
    if set(disposition_approval_refs) != set(approval_by_id):
        raise EvidenceError(
            "Every terminal approval must be referenced by the disposition, with no arbitrary refs."
        )
    if any(approval_times[item] > decided for item in disposition_approval_refs):
        raise EvidenceError("Disposition timestamp precedes referenced approval evidence.")
    if any(approval_times[item] < finished for item in disposition_approval_refs):
        raise EvidenceError("Terminal approval evidence must not predate exercise completion.")
    if any(imported > decided for imported in attestation_import_times.values()):
        raise EvidenceError("Disposition timestamp precedes imported attestation evidence.")
    if status == "passed":
        if decision != "approve":
            raise EvidenceError("A passed result requires an approved disposition.")
        if any(approval_by_id[item]["decision"] != "approve" for item in disposition_approval_refs):
            raise EvidenceError("A passed result may reference only approving evidence records.")
        if any(phase_by_name[name]["status"] != "passed" for name in exercise["requiredPhases"]):
            raise EvidenceError("Every required phase must pass before the result can pass.")
        if any(fault_by_name[name]["status"] != "passed" for name in exercise["requiredFaults"]):
            raise EvidenceError("Every required fault must pass before the result can pass.")
        if any(metric_by_name[item["name"]]["passed"] is not True for item in exercise["requiredMetrics"]):
            raise EvidenceError("Every required metric must pass before the result can pass.")
        artifact_kinds = {item["kind"] for item in document["artifacts"]}
        missing_artifacts = set(exercise["requiredArtifacts"]) - artifact_kinds
        if missing_artifacts:
            raise EvidenceError(
                f"Passed result is missing artifact kinds: {sorted(missing_artifacts)}"
            )
        referenced_approvals = [approval_by_id[item] for item in disposition_approval_refs]
        for role in exercise["requiredApproverRoles"]:
            if not any(
                approval["role"] == role
                and approval["decision"] == "approve"
                and document["runId"] in approval["subjects"]
                for approval in referenced_approvals
            ):
                raise EvidenceError(
                    f"Passed {exercise['type']} result lacks role-bound approval evidence "
                    f"from {role}."
                )

        required_attestation_pairs = {
            (item["type"], domain)
            for item in exercise["requiredAttestations"]
            for domain in item["requiredServiceDomains"]
        }
        actual_attestation_pairs = {
            (item["type"], item["serviceDomain"])
            for item in document["attestations"]
        }
        if actual_attestation_pairs != required_attestation_pairs:
            raise EvidenceError(
                "Passed result attestation service-domain coverage does not exactly match "
                "the exercise contract."
            )
        for requirement in exercise["requiredAttestations"]:
            attestations = [
                item
                for item in document["attestations"]
                if item["type"] == requirement["type"]
            ]
            required_attestation_ids = {item["id"] for item in attestations}
            for attestation in attestations:
                attestation_id = attestation["id"]
                for claim in requirement["requiredClaims"]:
                    claim_field = ATTESTATION_CLAIM_FIELDS[claim]
                    if attestation["claims"].get(claim_field) is not True:
                        raise EvidenceError(
                            f"Attestation {attestation_id} lacks required claim {claim}."
                        )
            approval_subjects = {document["runId"], *required_attestation_ids}
            latest_import = max(
                attestation_import_times[item] for item in required_attestation_ids
            )
            for role in requirement["requiredApprovalRoles"]:
                matching = [
                    approval
                    for approval in referenced_approvals
                    if approval["role"] == role
                    and approval["decision"] == "approve"
                    and approval_subjects.issubset(set(approval["subjects"]))
                ]
                if not matching:
                    raise EvidenceError(
                        f"The {role} approval is not bound to the run and every imported "
                        "production principal/grant attestation."
                    )
                if not any(
                    approval_times[item["id"]] >= latest_import
                    for item in matching
                ):
                    raise EvidenceError(
                        f"The {role} approval predates at least one imported production "
                        "principal/grant attestation."
                    )
        if not document["dataProfile"]["distributionVerified"]:
            raise EvidenceError("Passed result requires a verified representative distribution.")
        safety = document["safety"]
        required_safety = (
            "abortAuthorityPresent",
            "immutableEvidenceTargetConfigured",
            "destructiveStepsProviderApproved",
        )
        if any(not safety[field] for field in required_safety):
            raise EvidenceError("Passed result is missing a required safety attestation.")
        if exercise["requiresIsolatedTarget"] and not safety["isolatedTargetVerified"]:
            raise EvidenceError("Passed result requires an isolated target attestation.")
        if safety["writerFenceRequired"] != exercise["requiresWriterFence"]:
            raise EvidenceError("Result writer-fence requirement differs from the plan.")
        if exercise["requiresWriterFence"] and not safety["writerFenceVerified"]:
            raise EvidenceError("Passed result requires a verified writer fence.")
        if finished - started > timedelta(minutes=exercise["maximumDurationMinutes"]):
            raise EvidenceError("Passed result exceeded the planned maximum duration.")
    else:
        if decision not in {"reject", "repeat"}:
            raise EvidenceError("A failed or aborted result requires reject or repeat disposition.")
        if not any(
            approval_by_id[item]["decision"] == "reject"
            for item in disposition_approval_refs
        ):
            raise EvidenceError(
                "A failed or aborted result requires referenced rejecting approval evidence."
            )
    return document


def _phase(name: str) -> dict[str, Any]:
    return {
        "name": name,
        "status": "not-started",
        "startedAtUtc": None,
        "finishedAtUtc": None,
        "artifactRefs": [],
    }


def scaffold_result(args: argparse.Namespace, plan: dict[str, Any]) -> Path:
    if not SAFE_ID.fullmatch(args.run_id):
        raise EvidenceError("run-id must contain only letters, digits, dot, underscore, or dash.")
    if not SAFE_ID.fullmatch(args.target_environment):
        raise EvidenceError("target-environment has an invalid format.")
    if not SOURCE_REVISION.fullmatch(args.source_revision):
        raise EvidenceError("source-revision must be a 7-64 character hexadecimal revision.")
    if not IMAGE_DIGEST.fullmatch(args.image_digest):
        raise EvidenceError("image-digest must be a lowercase sha256 digest.")
    if not all(
        (
            args.acknowledge_isolated_target,
            args.acknowledge_sanitized_data,
            args.acknowledge_abort_authority,
            args.acknowledge_immutable_evidence,
            args.acknowledge_provider_approval,
        )
    ):
        raise EvidenceError("All explicit safety acknowledgements are required to scaffold a run.")

    exercise = _exercise(plan, args.exercise)
    output = Path(args.output).expanduser().resolve()
    if NET_CORE_ROOT == output or NET_CORE_ROOT in output.parents:
        raise EvidenceError("Execution evidence must be written outside the source repository.")
    if output.exists():
        raise EvidenceError(f"Refusing to overwrite existing evidence file: {output}")
    if not output.parent.is_dir():
        raise EvidenceError(f"Output directory must already exist: {output.parent}")

    now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    document: dict[str, Any] = {
        "$schema": "representative-exercise-result.schema.json",
        "schemaVersion": "2.0",
        "planId": plan["planId"],
        "runId": args.run_id,
        "exerciseType": exercise["type"],
        "gates": exercise["gates"],
        "status": "in-progress",
        "targetEnvironment": args.target_environment,
        "startedAtUtc": now,
        "finishedAtUtc": None,
        "changeId": args.change_id,
        "owner": args.owner,
        "reviewers": [],
        "sourceRevision": args.source_revision,
        "imageDigests": [args.image_digest],
        "infrastructureRevision": args.infrastructure_revision,
        "dataProfile": {
            "sanitized": True,
            "productionDataUsed": False,
            "generatorRevision": args.generator_revision,
            "distributionVerified": False,
            "cardinality": {},
        },
        "safety": {
            "targetIsProduction": False,
            "isolatedTargetVerified": True,
            "credentialsFromEnvironment": True,
            "writerFenceRequired": exercise["requiresWriterFence"],
            "writerFenceVerified": not exercise["requiresWriterFence"],
            "destructiveStepsProviderApproved": True,
            "abortAuthorityPresent": True,
            "immutableEvidenceTargetConfigured": True,
        },
        "phases": [_phase(name) for name in exercise["requiredPhases"]],
        "faults": [_phase(name) for name in exercise["requiredFaults"]],
        "metrics": [
            {
                "name": metric["name"],
                "unit": metric["unit"],
                "operator": metric["operator"],
                "target": metric["target"],
                "actual": None,
                "passed": None,
                "source": "pending",
            }
            for metric in exercise["requiredMetrics"]
        ],
        "artifacts": [],
        "attestations": [],
        "approvals": [],
        "residualRisks": [],
        "disposition": {
            "decision": "pending",
            "decidedAtUtc": None,
            "approvalRefs": [],
        },
    }
    validate_result(document, plan)
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(document, indent=2) + "\n")
    return output


def _self_test_skeleton(
    plan: dict[str, Any], exercise_type: str, run_id: str, now: str
) -> dict[str, Any]:
    exercise = _exercise(plan, exercise_type)
    return {
        "$schema": "representative-exercise-result.schema.json",
        "schemaVersion": "2.0",
        "planId": plan["planId"],
        "runId": run_id,
        "exerciseType": exercise["type"],
        "gates": exercise["gates"],
        "status": "in-progress",
        "targetEnvironment": plan["allowedTargetEnvironments"][0],
        "startedAtUtc": now,
        "finishedAtUtc": None,
        "changeId": run_id,
        "owner": "repository-validator",
        "reviewers": [],
        "sourceRevision": "0123456789abcdef",
        "imageDigests": ["sha256:" + "0" * 64],
        "infrastructureRevision": "validator-self-test",
        "dataProfile": {
            "sanitized": True,
            "productionDataUsed": False,
            "generatorRevision": "validator-self-test",
            "distributionVerified": False,
            "cardinality": {},
        },
        "safety": {
            "targetIsProduction": False,
            "isolatedTargetVerified": True,
            "credentialsFromEnvironment": True,
            "writerFenceRequired": exercise["requiresWriterFence"],
            "writerFenceVerified": not exercise["requiresWriterFence"],
            "destructiveStepsProviderApproved": True,
            "abortAuthorityPresent": True,
            "immutableEvidenceTargetConfigured": True,
        },
        "phases": [_phase(name) for name in exercise["requiredPhases"]],
        "faults": [_phase(name) for name in exercise["requiredFaults"]],
        "metrics": [
            {
                "name": metric["name"],
                "unit": metric["unit"],
                "operator": metric["operator"],
                "target": metric["target"],
                "actual": None,
                "passed": None,
                "source": "pending",
            }
            for metric in exercise["requiredMetrics"]
        ],
        "artifacts": [],
        "attestations": [],
        "approvals": [],
        "residualRisks": [],
        "disposition": {
            "decision": "pending",
            "decidedAtUtc": None,
            "approvalRefs": [],
        },
    }


def _complete_self_test_result(
    skeleton: dict[str, Any], plan: dict[str, Any], now: str
) -> dict[str, Any]:
    result = copy.deepcopy(skeleton)
    exercise = _exercise(plan, result["exerciseType"])
    result["status"] = "passed"
    result["finishedAtUtc"] = now
    result["reviewers"] = ["repository-validator"]
    result["dataProfile"]["distributionVerified"] = True
    result["safety"]["writerFenceVerified"] = True
    for item in (*result["phases"], *result["faults"]):
        item["status"] = "passed"
        item["startedAtUtc"] = now
        item["finishedAtUtc"] = now
    for metric in result["metrics"]:
        metric["actual"] = metric["target"]
        metric["passed"] = True
        metric["source"] = "validator-self-test"

    attestation_domains = [
        domain
        for requirement in exercise["requiredAttestations"]
        for domain in requirement["requiredServiceDomains"]
    ]
    if attestation_domains:
        result["imageDigests"] = [
            "sha256:" + f"{1000 + index:064x}"
            for index, _ in enumerate(attestation_domains)
        ]

    retention = (
        datetime.now(timezone.utc) + timedelta(days=plan["evidenceRetentionDays"] + 1)
    ).isoformat().replace("+00:00", "Z")
    result["artifacts"] = [
        {
            "id": f"artifact-{kind}",
            "kind": kind,
            "uri": f"artifact://validator-self-test/{result['runId']}/{kind}",
            "sha256": f"{index:064x}",
            "retentionUntilUtc": retention,
        }
        for index, kind in enumerate(exercise["requiredArtifacts"], start=1)
    ]
    artifact_id_by_kind = {
        artifact["kind"]: artifact["id"] for artifact in result["artifacts"]
    }

    for requirement in exercise["requiredAttestations"]:
        evidence_kinds = (
            "command-log",
            "grant-report",
            "production-grant-policy-export",
            "production-principal-attestation",
        )
        for domain_index, domain in enumerate(requirement["requiredServiceDomains"]):
            if domain_index == 0:
                domain_artifacts = {
                    kind: artifact_id_by_kind[kind] for kind in evidence_kinds
                }
            else:
                domain_artifacts = {}
                for kind in evidence_kinds:
                    artifact_id = f"artifact-{kind}-{domain}"
                    artifact_number = len(result["artifacts"]) + 1
                    result["artifacts"].append(
                        {
                            "id": artifact_id,
                            "kind": kind,
                            "uri": (
                                f"artifact://validator-self-test/{result['runId']}/"
                                f"{domain}/{kind}"
                            ),
                            "sha256": f"{artifact_number:064x}",
                            "retentionUntilUtc": retention,
                        }
                    )
                    domain_artifacts[kind] = artifact_id

            attestation_id = f"production-principal-grant-{domain}"
            result["attestations"].append(
                {
                    "id": attestation_id,
                    "type": requirement["type"],
                    "sourceEnvironment": "production",
                    "serviceDomain": domain,
                    "observedAtUtc": now,
                    "importedAtUtc": now,
                    "productionEnvironmentRevision": (
                        "sha256:" + f"{2000 + domain_index:064x}"
                    ),
                    "principalIdSha256": f"{3000 + domain_index:064x}",
                    "deployedIdentityRevision": (
                        "sha256:" + f"{4000 + domain_index:064x}"
                    ),
                    "secretRevisionIdSha256": f"{5000 + domain_index:064x}",
                    "workloadRevision": result["imageDigests"][domain_index],
                    "grantPolicyRevision": (
                        "sha256:" + f"{6000 + domain_index:064x}"
                    ),
                    "grantPolicyExportArtifactRef": domain_artifacts[
                        "production-grant-policy-export"
                    ],
                    "attestationArtifactRef": domain_artifacts[
                        "production-principal-attestation"
                    ],
                    "claims": {
                        "runtimeDdlDenied": True,
                        "runtimeDmlSmokePassed": True,
                        "migrationOnlyDdlVerified": True,
                        "crossRoleIsolationVerified": True,
                    },
                    "claimEvidenceRefs": {
                        "runtimeDdlDenied": domain_artifacts["command-log"],
                        "runtimeDmlSmokePassed": domain_artifacts["command-log"],
                        "migrationOnlyDdlVerified": domain_artifacts["grant-report"],
                        "crossRoleIsolationVerified": domain_artifacts["grant-report"],
                    },
                }
            )

    required_attestation_roles = {
        role
        for requirement in exercise["requiredAttestations"]
        for role in requirement["requiredApprovalRoles"]
    }
    attestation_ids = [item["id"] for item in result["attestations"]]
    result["approvals"] = []
    for role in exercise["requiredApproverRoles"]:
        subjects = [result["runId"]]
        if role in required_attestation_roles:
            subjects.extend(attestation_ids)
        result["approvals"].append(
            {
                "id": f"approval-{role}",
                "role": role,
                "decision": "approve",
                "subjects": subjects,
                "decidedAtUtc": now,
                "evidenceRef": artifact_id_by_kind["approval-record"],
                "boundSourceRevision": result["sourceRevision"],
                "boundImageDigests": result["imageDigests"],
                "boundInfrastructureRevision": result["infrastructureRevision"],
            }
        )
    result["disposition"] = {
        "decision": "approve",
        "decidedAtUtc": now,
        "approvalRefs": [item["id"] for item in result["approvals"]],
    }
    return result


def self_test(plan: dict[str, Any]) -> None:
    now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    skeleton = _self_test_skeleton(
        plan, "projection-rebuild", "validator-self-test", now
    )
    validate_result(skeleton, plan)

    valid_pass = _complete_self_test_result(skeleton, plan, now)
    validate_result(valid_pass, plan)

    ddl_skeleton = _self_test_skeleton(
        plan, "ddl-role-separation", "validator-self-test-ddl", now
    )
    valid_ddl = _complete_self_test_result(ddl_skeleton, plan, now)
    validate_result(valid_ddl, plan)

    cache_skeleton = _self_test_skeleton(
        plan, "cache-zero-backlog", "validator-self-test-cache", now
    )
    valid_cache = _complete_self_test_result(cache_skeleton, plan, now)
    validate_result(valid_cache, plan)

    def expect_rejected(document: dict[str, Any], message: str) -> None:
        try:
            validate_result(document, plan)
        except EvidenceError:
            return
        raise EvidenceError(f"Self-test failed: {message}")

    production = copy.deepcopy(ddl_skeleton)
    production["targetEnvironment"] = "production"
    expect_rejected(production, "production execution target was accepted.")

    false_pass = copy.deepcopy(skeleton)
    false_pass["status"] = "passed"
    false_pass["finishedAtUtc"] = now
    false_pass["disposition"] = {
        "decision": "approve",
        "decidedAtUtc": now,
        "approvalRefs": ["arbitrary-string"],
    }
    expect_rejected(false_pass, "arbitrary approval string passed a result.")

    reviewer_only = copy.deepcopy(valid_pass)
    reviewer_only["reviewers"] = list(_exercise(plan, "projection-rebuild")["requiredApproverRoles"])
    reviewer_only["approvals"] = []
    reviewer_only["disposition"]["approvalRefs"] = []
    expect_rejected(reviewer_only, "reviewer strings substituted for approval evidence.")

    missing_role = copy.deepcopy(valid_pass)
    removed_approval = missing_role["approvals"].pop()
    missing_role["disposition"]["approvalRefs"].remove(removed_approval["id"])
    expect_rejected(missing_role, "a passed result omitted a required approver role.")

    wrong_approval_artifact = copy.deepcopy(valid_pass)
    approval_artifact = next(
        item for item in wrong_approval_artifact["artifacts"]
        if item["kind"] == "approval-record"
    )
    approval_artifact["kind"] = "command-log"
    expect_rejected(
        wrong_approval_artifact,
        "approval evidence referenced a non-approval artifact.",
    )

    missing_attestation = copy.deepcopy(valid_ddl)
    missing_attestation["attestations"].pop()
    expect_rejected(
        missing_attestation,
        "ddl-role-separation passed with a missing service-domain attestation.",
    )

    duplicate_attestation_domain = copy.deepcopy(valid_ddl)
    duplicate_domain_entry = copy.deepcopy(duplicate_attestation_domain["attestations"][0])
    duplicate_domain_entry["id"] = "production-principal-grant-comment-duplicate"
    duplicate_attestation_domain["attestations"].append(duplicate_domain_entry)
    expect_rejected(
        duplicate_attestation_domain,
        "ddl-role-separation accepted duplicate service-domain attestations.",
    )

    wrong_attestation_domain = copy.deepcopy(valid_ddl)
    moderation_attestation = next(
        item for item in wrong_attestation_domain["attestations"]
        if item["serviceDomain"] == "moderation"
    )
    moderation_attestation["serviceDomain"] = "feed"
    expect_rejected(
        wrong_attestation_domain,
        "ddl-role-separation accepted wrong service-domain coverage.",
    )

    wrong_workload = copy.deepcopy(valid_ddl)
    wrong_workload["attestations"][0]["workloadRevision"] = "sha256:" + "f" * 64
    expect_rejected(
        wrong_workload,
        "production attestation was not bound to the deployed workload revision.",
    )

    unbound_attestation_approval = copy.deepcopy(valid_ddl)
    attestation_id = unbound_attestation_approval["attestations"][0]["id"]
    database_approval = next(
        item for item in unbound_attestation_approval["approvals"]
        if item["role"] == "database-engineering"
    )
    database_approval["subjects"].remove(attestation_id)
    expect_rejected(
        unbound_attestation_approval,
        "database approval was not bound to imported production attestation.",
    )

    wrong_claim_evidence = copy.deepcopy(valid_ddl)
    wrong_claim_evidence["attestations"][0]["claimEvidenceRefs"][
        "crossRoleIsolationVerified"
    ] = "artifact-command-log"
    expect_rejected(
        wrong_claim_evidence,
        "cross-role isolation claim referenced the wrong evidence kind.",
    )

    cache_without_shedding = copy.deepcopy(valid_cache)
    shed_metric = next(
        item for item in cache_without_shedding["metrics"]
        if item["name"] == "cachezero.shed-responses-observed"
    )
    shed_metric["actual"] = 0
    shed_metric["passed"] = False
    expect_rejected(
        cache_without_shedding,
        "cache-zero result passed without observed overload shedding.",
    )

    duplicate_phase = copy.deepcopy(skeleton)
    second_phase = copy.deepcopy(duplicate_phase["phases"][0])
    second_phase["status"] = "running"
    duplicate_phase["phases"].append(second_phase)
    expect_rejected(duplicate_phase, "duplicate phase name was accepted.")

    duplicate_metric = copy.deepcopy(skeleton)
    second_metric = copy.deepcopy(duplicate_metric["metrics"][0])
    second_metric["source"] = "different-source"
    duplicate_metric["metrics"].append(second_metric)
    expect_rejected(duplicate_metric, "duplicate metric name was accepted.")

    dangling_artifact = copy.deepcopy(skeleton)
    dangling_artifact["phases"][0]["artifactRefs"] = ["missing-artifact"]
    expect_rejected(dangling_artifact, "dangling artifact reference was accepted.")

    weakened_cache_plan = copy.deepcopy(plan)
    weakened_cache = _exercise(weakened_cache_plan, "cache-zero-backlog")
    weakened_cache["requiredMetrics"] = [
        item
        for item in weakened_cache["requiredMetrics"]
        if item["name"] != "cachezero.shed-responses-observed"
    ]
    try:
        validate_plan(weakened_cache_plan)
    except EvidenceError:
        pass
    else:
        raise EvidenceError(
            "Self-test failed: cache overload-shedding plan contract was weakened."
        )

    weakened_ddl_plan = copy.deepcopy(plan)
    _exercise(weakened_ddl_plan, "ddl-role-separation")["requiredAttestations"] = []
    try:
        validate_plan(weakened_ddl_plan)
    except EvidenceError:
        pass
    else:
        raise EvidenceError(
            "Self-test failed: DDL production-attestation plan contract was weakened."
        )

    weakened_ddl_domain_plan = copy.deepcopy(plan)
    ddl_requirement = _exercise(
        weakened_ddl_domain_plan, "ddl-role-separation"
    )["requiredAttestations"][0]
    ddl_requirement["requiredServiceDomains"].remove("moderation")
    try:
        validate_plan(weakened_ddl_domain_plan)
    except EvidenceError:
        pass
    else:
        raise EvidenceError(
            "Self-test failed: DDL service-domain attestation coverage was weakened."
        )


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    commands = result.add_subparsers(dest="command", required=True)

    commands.add_parser("validate-plan")
    validate_result_parser = commands.add_parser("validate-result")
    validate_result_parser.add_argument("result", type=Path)

    scaffold = commands.add_parser("scaffold")
    scaffold.add_argument("--exercise", choices=sorted(EXERCISE_TYPES), required=True)
    scaffold.add_argument("--run-id", required=True)
    scaffold.add_argument("--target-environment", required=True)
    scaffold.add_argument("--change-id", required=True)
    scaffold.add_argument("--owner", required=True)
    scaffold.add_argument("--source-revision", required=True)
    scaffold.add_argument("--image-digest", required=True)
    scaffold.add_argument("--infrastructure-revision", required=True)
    scaffold.add_argument("--generator-revision", required=True)
    scaffold.add_argument("--output", required=True)
    scaffold.add_argument("--acknowledge-isolated-target", action="store_true")
    scaffold.add_argument("--acknowledge-sanitized-data", action="store_true")
    scaffold.add_argument("--acknowledge-abort-authority", action="store_true")
    scaffold.add_argument("--acknowledge-immutable-evidence", action="store_true")
    scaffold.add_argument("--acknowledge-provider-approval", action="store_true")

    commands.add_parser("self-test")
    return result


def main() -> int:
    args = parser().parse_args()
    try:
        plan = validate_plan(load_json(args.plan.resolve()))
        if args.command == "validate-plan":
            print(
                f"Validated {len(plan['exercises'])} exercises covering "
                f"{len(EXPECTED_GATES)} open evidence gates."
            )
        elif args.command == "validate-result":
            result = validate_result(load_json(args.result.resolve()), plan)
            print(
                f"Validated {result['exerciseType']} result {result['runId']} "
                f"with status {result['status']}."
            )
        elif args.command == "scaffold":
            output = scaffold_result(args, plan)
            print(f"Created sanitized in-progress evidence outside the repository: {output}")
        elif args.command == "self-test":
            self_test(plan)
            print("Representative evidence validator self-test passed.")
        else:  # pragma: no cover
            raise EvidenceError(f"Unsupported command: {args.command}")
        return 0
    except EvidenceError as error:
        print(f"Evidence validation failed: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
