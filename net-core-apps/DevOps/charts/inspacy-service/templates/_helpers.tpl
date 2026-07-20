{{- define "inspacy-service.name" -}}
{{- default .Chart.Name .Values.service.name | trunc 30 | trimSuffix "-" -}}
{{- end -}}

{{- define "inspacy-service.fullname" -}}
{{- printf "%s-%s" .Release.Name (include "inspacy-service.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "inspacy-service.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "inspacy-service.commonLabels" -}}
helm.sh/chart: {{ include "inspacy-service.chart" . }}
app.kubernetes.io/name: {{ include "inspacy-service.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: inspacy
inspacy.io/service: {{ .Values.service.name }}
{{- with .Values.service.labels }}
{{ toYaml . }}
{{- end }}
{{- end -}}

{{- define "inspacy-service.selectorLabels" -}}
app.kubernetes.io/name: {{ include "inspacy-service.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
inspacy.io/service: {{ .root.Values.service.name }}
inspacy.io/runtime-role: {{ .role }}
{{- end -}}

{{- define "inspacy-service.runtimeRole" -}}
{{- if eq . "api" -}}Api
{{- else if eq . "consumer" -}}ConsumerWorker
{{- else if eq . "job" -}}JobWorker
{{- else if eq . "migration" -}}Migration
{{- else -}}{{ fail (printf "unsupported runtime role %q" .) }}
{{- end -}}
{{- end -}}

{{- define "inspacy-service.image" -}}
{{- $repository := required "service.image.repository is required when service.enabled=true" .Values.service.image.repository -}}
{{- $digest := required "service.image.digest is required when service.enabled=true" .Values.service.image.digest -}}
{{- if not (regexMatch "^sha256:[0-9a-f]{64}$" $digest) -}}
{{- fail "service.image.digest must be a lowercase sha256 digest; mutable tags are forbidden" -}}
{{- end -}}
{{- printf "%s@%s" $repository $digest -}}
{{- end -}}

{{- define "inspacy-service.roleName" -}}
{{- printf "%s-%s" (include "inspacy-service.fullname" .root) .role | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "inspacy-service.podSecurityContext" -}}
runAsNonRoot: true
runAsUser: 1654
runAsGroup: 1654
fsGroup: 1654
fsGroupChangePolicy: OnRootMismatch
seccompProfile:
  type: RuntimeDefault
{{- end -}}

{{- define "inspacy-service.containerSecurityContext" -}}
allowPrivilegeEscalation: false
privileged: false
readOnlyRootFilesystem: true
runAsNonRoot: true
runAsUser: 1654
runAsGroup: 1654
appArmorProfile:
  type: RuntimeDefault
capabilities:
  drop: ["ALL"]
{{- end -}}

{{- define "inspacy-service.validate" -}}
{{- if .Values.service.enabled -}}
{{- if ge (int .Values.rollout.preStopDelaySeconds) (int .Values.rollout.terminationGracePeriodSeconds) -}}
{{- fail "rollout.preStopDelaySeconds must be smaller than rollout.terminationGracePeriodSeconds" -}}
{{- end -}}
{{- if .Values.networkPolicy.enabled -}}
{{- if ne (empty .Values.networkPolicy.edgeNamespaceSelector) (empty .Values.networkPolicy.edgePodSelector) -}}
{{- fail "networkPolicy.edgeNamespaceSelector and edgePodSelector must either both be populated or both be empty" -}}
{{- end -}}
{{- if ne (empty .Values.networkPolicy.monitoringNamespaceSelector) (empty .Values.networkPolicy.monitoringPodSelector) -}}
{{- fail "networkPolicy.monitoringNamespaceSelector and monitoringPodSelector must either both be populated or both be empty" -}}
{{- end -}}
{{- if and .Values.migration.enabled (eq (add (len .Values.networkPolicy.migrationClusterEgress) (len .Values.networkPolicy.migrationExternalEgress)) 0) -}}
{{- fail "an enabled migration requires at least one dedicated migrationClusterEgress or migrationExternalEgress destination" -}}
{{- end -}}
{{- end -}}
{{- $secretOwners := dict -}}
{{- range $role := list "api" "consumer" "job" -}}
{{- $settings := index $.Values.roles $role -}}
{{- if $settings.enabled -}}
{{- $secretName := required (printf "roles.%s.secretName is required when that role is enabled" $role) $settings.secretName -}}
{{- if hasKey $secretOwners $secretName -}}
{{- fail (printf "enabled roles must use distinct Secrets; roles.%s.secretName reuses %q from %s" $role $secretName (index $secretOwners $secretName)) -}}
{{- end -}}
{{- $_ := set $secretOwners $secretName (printf "roles.%s" $role) -}}
{{- if $settings.autoscaling.enabled -}}
{{- if gt (int $settings.autoscaling.minReplicas) (int $settings.autoscaling.maxReplicas) -}}
{{- fail (printf "roles.%s.autoscaling.minReplicas cannot exceed maxReplicas" $role) -}}
{{- end -}}
{{- if eq (len $settings.autoscaling.metrics) 0 -}}
{{- fail (printf "roles.%s.autoscaling.metrics must not be empty when autoscaling is enabled" $role) -}}
{{- end -}}
{{- end -}}
{{- end -}}
{{- end -}}
{{- if .Values.migration.enabled -}}
{{- $migrationSecret := required "migration.secretName is required when migration.enabled=true" .Values.migration.secretName -}}
{{- if hasKey $secretOwners $migrationSecret -}}
{{- fail (printf "migration.secretName must be distinct from runtime role Secrets; %q is already used by %s" $migrationSecret (index $secretOwners $migrationSecret)) -}}
{{- end -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "inspacy-service.egressRules" -}}
- to:
    - namespaceSelector:
        matchLabels:
{{ toYaml .Values.networkPolicy.dns.namespaceSelector | indent 10 }}
      podSelector:
        matchLabels:
{{ toYaml .Values.networkPolicy.dns.podSelector | indent 10 }}
  ports:
    - { protocol: UDP, port: 53 }
    - { protocol: TCP, port: 53 }
{{- range .Values.networkPolicy.clusterEgress }}
- to:
    - namespaceSelector:
        matchLabels:
{{ toYaml .namespaceSelector | indent 10 }}
      podSelector:
        matchLabels:
{{ toYaml .podSelector | indent 10 }}
  ports:
{{ toYaml .ports | indent 4 }}
{{- end }}
{{- range .Values.networkPolicy.externalEgress }}
- to:
    - ipBlock:
        cidr: {{ .cidr }}
{{- with .except }}
        except:
{{ toYaml . | indent 10 }}
{{- end }}
  ports:
{{ toYaml .ports | indent 4 }}
{{- end }}
{{- end -}}

{{- define "inspacy-service.migrationEgressRules" -}}
- to:
    - namespaceSelector:
        matchLabels:
{{ toYaml .Values.networkPolicy.dns.namespaceSelector | indent 10 }}
      podSelector:
        matchLabels:
{{ toYaml .Values.networkPolicy.dns.podSelector | indent 10 }}
  ports:
    - { protocol: UDP, port: 53 }
    - { protocol: TCP, port: 53 }
{{- range .Values.networkPolicy.migrationClusterEgress }}
- to:
    - namespaceSelector:
        matchLabels:
{{ toYaml .namespaceSelector | indent 10 }}
      podSelector:
        matchLabels:
{{ toYaml .podSelector | indent 10 }}
  ports:
{{ toYaml .ports | indent 4 }}
{{- end }}
{{- range .Values.networkPolicy.migrationExternalEgress }}
- to:
    - ipBlock:
        cidr: {{ .cidr }}
{{- with .except }}
        except:
{{ toYaml . | indent 10 }}
{{- end }}
  ports:
{{ toYaml .ports | indent 4 }}
{{- end }}
{{- end -}}
