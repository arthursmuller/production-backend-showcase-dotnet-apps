#!/usr/bin/env bash
set -euo pipefail

bundle_dir=""
evidence_dir=""
service=""
source_commit=""
source_run_id=""
repository=""
expected_archive_digest=""
expected_image_digest=""

fail() {
  echo "promotion verification failed: $*" >&2
  exit 1
}

usage() {
  echo "usage: $0 --bundle-dir DIR --evidence-dir DIR --service NAME --source-commit SHA --source-run-id ID --repository OWNER/REPO --expected-archive-digest sha256:HEX --expected-image-digest sha256:HEX" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bundle-dir) bundle_dir="${2:-}"; shift 2 ;;
    --evidence-dir) evidence_dir="${2:-}"; shift 2 ;;
    --service) service="${2:-}"; shift 2 ;;
    --source-commit) source_commit="${2:-}"; shift 2 ;;
    --source-run-id) source_run_id="${2:-}"; shift 2 ;;
    --repository) repository="${2:-}"; shift 2 ;;
    --expected-archive-digest) expected_archive_digest="${2:-}"; shift 2 ;;
    --expected-image-digest) expected_image_digest="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done

[[ "$service" =~ ^(comment-api|feed-api|following-api|interaction-api|moderation-api|rabbitmq-delayed)$ ]] || fail "unsupported service"
[[ "$source_commit" =~ ^[0-9a-f]{40}$ ]] || fail "source commit must be a full lowercase Git SHA"
[[ "$source_run_id" =~ ^[1-9][0-9]*$ ]] || fail "source run id must be numeric"
[[ "$repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "repository must be OWNER/REPO"
[[ "$expected_archive_digest" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "archive digest must be sha256:HEX"
[[ "$expected_image_digest" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "image digest must be sha256:HEX"
[[ -d "$bundle_dir" && ! -L "$bundle_dir" ]] || fail "bundle directory is missing or is a symlink"
[[ -n "$evidence_dir" ]] || fail "evidence directory is required"

for required_command in gh jq sha256sum tar date; do
  command -v "$required_command" >/dev/null 2>&1 || fail "$required_command is required"
done

mkdir -p "$evidence_dir"
[[ -d "$evidence_dir" && ! -L "$evidence_dir" ]] || fail "evidence directory is invalid"

archive="$bundle_dir/${service}.tar"
checksum="$bundle_dir/${service}.tar.sha256"
metadata="$bundle_dir/${service}.release.json"
sbom="$bundle_dir/${service}.spdx.json"

for bundle_file in "$archive" "$checksum" "$metadata" "$sbom"; do
  [[ -f "$bundle_file" && ! -L "$bundle_file" ]] || fail "missing regular bundle file: $bundle_file"
done

mapfile -t checksum_lines < "$checksum"
[[ "${#checksum_lines[@]}" -eq 1 ]] || fail "checksum file must contain exactly one entry"
expected_checksum_line="${expected_archive_digest#sha256:}  ${service}.tar"
[[ "${checksum_lines[0]%$'\r'}" == "$expected_checksum_line" ]] || fail "checksum file does not match the approved archive digest and filename"
(cd "$bundle_dir" && sha256sum --check --strict --status "${service}.tar.sha256") || fail "archive checksum verification failed"

archive_attestation="$evidence_dir/archive-provenance.json"
metadata_attestation="$evidence_dir/metadata-provenance.json"
sbom_attestation="$evidence_dir/sbom-attestation.json"
signer_workflow="$repository/.github/workflows/backend-ci.yml"

gh attestation verify "$archive" \
  --repo "$repository" \
  --signer-workflow "$signer_workflow" \
  --format json > "$archive_attestation"
gh attestation verify "$metadata" \
  --repo "$repository" \
  --signer-workflow "$signer_workflow" \
  --format json > "$metadata_attestation"
gh attestation verify "$archive" \
  --repo "$repository" \
  --signer-workflow "$signer_workflow" \
  --predicate-type https://spdx.dev/Document/v2.3 \
  --format json > "$sbom_attestation"

for attestation in "$archive_attestation" "$metadata_attestation" "$sbom_attestation"; do
  jq -e 'type == "array" and length > 0' "$attestation" >/dev/null || fail "empty or invalid attestation result: $attestation"
done

actual_sbom_digest="sha256:$(sha256sum "$sbom" | awk '{print $1}')"
jq -e --slurpfile localSbom "$sbom" '
  length > 0 and all(.[]; .verificationResult.statement.predicate == $localSbom[0])
' "$sbom_attestation" >/dev/null || fail "downloaded SBOM differs from the signed SPDX predicate"
jq -e '(.spdxVersion | startswith("SPDX-2.")) and ((.packages // []) | length > 0)' "$sbom" >/dev/null || fail "SBOM is not a non-empty SPDX 2 document"

jq -e \
  --arg service "$service" \
  --arg sourceCommit "$source_commit" \
  --arg sourceRunId "$source_run_id" \
  --arg archiveDigest "$expected_archive_digest" \
  --arg imageDigest "$expected_image_digest" \
  --arg sbomDigest "$actual_sbom_digest" '
    .schemaVersion == 1 and
    .status == "build-evidence-not-deployment-evidence" and
    .artifactName == $service and
    .sourceCommit == $sourceCommit and
    .sourceRunId == $sourceRunId and
    (.sourceWorkflow | contains("/.github/workflows/backend-ci.yml@")) and
    .archive.file == ($service + ".tar") and
    .archive.sha256 == $archiveDigest and
    .image.digest == $imageDigest and
    .sbom.file == ($service + ".spdx.json") and
    .sbom.sha256 == $sbomDigest
  ' "$metadata" >/dev/null || fail "signed release metadata does not match the approved subject"

index_json="$(mktemp)"
trap 'rm -f "$index_json"' EXIT
[[ "$(tar -tf "$archive" | awk '$0 == "index.json" { count++ } END { print count + 0 }')" == "1" ]] || fail "OCI archive must contain exactly one root index.json"
tar -xOf "$archive" index.json > "$index_json"
jq -e '.schemaVersion == 2 and (.manifests | type == "array" and length > 0)' "$index_json" >/dev/null || fail "invalid OCI index"
actual_index_digest="sha256:$(sha256sum "$index_json" | awk '{print $1}')"
jq -e --arg digest "$actual_index_digest" '.image.ociIndexDigest == $digest' "$metadata" >/dev/null || fail "signed OCI index digest does not match the archive"
if [[ "$expected_image_digest" != "$actual_index_digest" ]] &&
   ! jq -e --arg digest "$expected_image_digest" '.manifests | any(.digest == $digest)' "$index_json" >/dev/null; then
  fail "approved image digest is not represented by the OCI archive"
fi

jq -n \
  --arg verifiedAtUtc "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" \
  --arg repository "$repository" \
  --arg service "$service" \
  --arg sourceCommit "$source_commit" \
  --arg sourceRunId "$source_run_id" \
  --arg archiveDigest "$expected_archive_digest" \
  --arg imageDigest "$expected_image_digest" \
  --arg sbomDigest "$actual_sbom_digest" \
  '{
    schemaVersion: 1,
    status: "repository-verified-not-deployed",
    verifiedAtUtc: $verifiedAtUtc,
    repository: $repository,
    service: $service,
    sourceCommit: $sourceCommit,
    sourceRunId: $sourceRunId,
    archiveDigest: $archiveDigest,
    imageDigest: $imageDigest,
    sbomDigest: $sbomDigest,
    signedProvenanceVerified: true,
    signedSpdxSbomVerified: true,
    signerWorkflow: ".github/workflows/backend-ci.yml",
    deploymentExecuted: false
  }' > "$evidence_dir/repository-verification.json"

echo "Promotion bundle signature, provenance, SBOM, archive digest, and OCI digest verified."
