#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_dir/../../.." && pwd)"
canary_policy="$script_dir/canary-rollout-policy.json"
edge_policy="$script_dir/edge-controls.example.json"
promotion_workflow="$repository_root/.github/workflows/backend-promotion-gate.yml"
build_workflow="$repository_root/.github/workflows/backend-ci.yml"
promotion_verifier="$script_dir/verify-promotion-bundle.sh"

fail() {
  echo "security control validation failed: $*" >&2
  exit 1
}

command -v jq >/dev/null 2>&1 || fail "jq is required"

for required_file in \
  "$canary_policy" \
  "$edge_policy" \
  "$promotion_workflow" \
  "$build_workflow" \
  "$promotion_verifier"; do
  [[ -f "$required_file" ]] || fail "missing $required_file"
done

jq -e '
  .schemaVersion == 1 and
  .status == "repository-policy-not-deployment-evidence" and
  .candidateIdentity.requireImageDigest == true and
  .candidateIdentity.requirePreviousKnownGoodDigest == true and
  .candidateIdentity.allowMutableTags == false and
  .candidateIdentity.requireSignedProvenance == true and
  .candidateIdentity.requireSignedSpdxSbom == true and
  ([.stages[].trafficPercent] == [1, 10, 25, 100]) and
  all(.stages[]; .minimumObservationSeconds >= 900) and
  ([
    "artifact-integrity",
    "edge-security",
    "schema-compatibility",
    "slo-error-budget",
    "latency",
    "dependency-health",
    "projection-convergence",
    "privacy-authorization"
  ] - [.requiredGates[].id] | length == 0) and
  all(.requiredGates[]; .failureAction == "block-promotion" or (.failureAction | contains("rollback")) or (.failureAction | contains("roll-forward"))) and
  .rollback.automatic == true and
  .rollback.target == "previous-known-good-image-digest-and-configuration-revision" and
  (.rollback.actions | index("route-new-traffic-to-previous-known-good-digest") != null) and
  (.rollback.forbiddenActions | index("run-an-unreviewed-database-down-migration") != null) and
  (.externalEvidenceRequired | length >= 5)
' "$canary_policy" >/dev/null || fail "invalid canary/rollback policy"

jq -e '
  .schemaVersion == 1 and
  .deploymentStatus == "example-not-deployed" and
  .providerBinding.status == "required-before-production" and
  .providerBinding.exportDigestRequired == true and
  .networkBoundary.managedDdosProtectionRequired == true and
  .networkBoundary.privateOriginRequired == true and
  .networkBoundary.directPublicOriginAccessAllowed == false and
  .tls.minimumVersion == "1.2" and
  .forwardedHeaders.mode == "overwrite-at-edge" and
  .forwardedHeaders.acceptOnlyFromConfiguredProxyRanges == true and
  (.forwardedHeaders.documentationOnlyProxyRanges == ["192.0.2.0/24"]) and
  (.forwardedHeaders.stripCallerIdentityHeaders | length >= 4) and
  .waf.managedRuleSetsRequired == true and
  .waf.failOpenAllowed == false and
  .distributedRateLimit.required == true and
  .distributedRateLimit.boundedPartitionCardinalityRequired == true and
  .distributedRateLimit.attackerControlledHeaderPartitionAllowed == false and
  .distributedRateLimit.overLimitResponseStatus == 429 and
  .routeControls.canonicalOpenApiInventoryRequired == true and
  .routeControls.defaultPublicRouteAction == "deny" and
  .logging.requestBodiesAllowed == false and
  .logging.authorizationHeadersAllowed == false and
  (.validationRequired | length >= 7)
' "$edge_policy" >/dev/null || fail "invalid managed edge/WAF example"

bash -n "$promotion_verifier" || fail "promotion verifier has invalid shell syntax"
bash -n "$0" || fail "security policy validator has invalid shell syntax"

grep -Fq 'actions/attest@' "$build_workflow" || fail "build workflow does not create an SBOM attestation"
grep -Fq 'https://spdx.dev/Document/v2.3' "$build_workflow" || fail "build workflow does not verify the SPDX attestation"
grep -Fq 'environment:' "$promotion_workflow" || fail "promotion workflow is not environment-gated"
grep -Fq 'verify-promotion-bundle.sh' "$promotion_workflow" || fail "promotion workflow does not invoke the bundle verifier"
grep -Fq -- '--signer-workflow' "$promotion_verifier" || fail "promotion verifier does not constrain the signing workflow"

echo "Repository security, edge, supply-chain, and canary policies are structurally valid."
