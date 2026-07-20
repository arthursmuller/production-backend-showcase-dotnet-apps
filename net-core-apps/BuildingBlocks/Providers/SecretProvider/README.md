# SecretProvider

`SecretProvider` resolves configuration placeholders at API startup by calling a
mutually authenticated HTTPS API. Server workloads load the client certificate either
from the operating-system certificate store or from a tightly permissioned PKCS#12 file
mounted by the workload platform.

```json
{
  "Identity": {
    "ClientSecret": "{{secret:IdentityClientSecret}}"
  },
  "Database": {
    "MariaDbSqlConnection": "{{secret:DatabaseConnectionString}}"
  },
  "Broker": {
    "RabbitMq": {
      "host": "rabbitmq",
      "port": 5672,
      "username": "feed-api",
      "password": "{{secret:BrokerPassword}}",
      "virtualHost": "inspacy"
    }
  },
  "SecretProvider": {
    "Enabled": true,
    "BaseAddress": "https://secrets.internal.example/",
    "EndpointTemplate": "api/secrets/{namespace}/{key}",
    "RequestTimeout": "00:00:10",
    "MaxAttempts": 3,
    "MaxResponseBytes": 65536,
    "References": {
      "IdentityClientSecret": {
        "Namespace": "identityConfig",
        "Key": "feed_client_secret",
        "Roles": [ "Api", "Combined" ]
      },
      "DatabaseConnectionString": {
        "Namespace": "feedConfig",
        "Key": "sql_connection_string",
        "Roles": [ "Api", "ConsumerWorker", "JobWorker", "Migration", "Combined" ]
      },
      "BrokerPassword": {
        "Namespace": "feedConfig",
        "Key": "rmq_password",
        "Roles": [ "Api", "ConsumerWorker", "JobWorker", "Combined" ]
      }
    },
    "Certificate": {
      "Path": "/var/run/secrets/inspacy/secret-provider/client.pfx"
    }
  }
}
```

Only the configured alias form is accepted; direct namespace/key placeholders
are intentionally unsupported so role authorization remains centralized.
Malformed values containing the reserved `{{secret` marker fail startup.

The endpoint must return either a non-empty JSON string or an object whose `value`
property is a non-empty string. Each distinct reference is fetched once during
startup. Secret values are held only in the immutable in-memory configuration snapshot;
they are never written back to appsettings or included in exception messages.

When `Enabled` is omitted, configuring `BaseAddress` activates the provider
automatically; `Enabled=false` is an explicit opt-out. Production fails fast when
unresolved placeholders remain and no provider endpoint is configured. Leave
`Optional=false` so missing certificates, inaccessible endpoints, and malformed
responses stop startup. TLS server-certificate validation and revocation checking
remain enabled; insecure HTTP requires an explicit development-only opt-in.
`AllowInsecureHttp=true` is rejected by the shared host in Production even when the
configured address happens to be HTTPS.

Container certificate paths must be absolute. PKCS#12 keys are loaded with ephemeral
key storage, so the runtime does not persist private-key material into its read-only
filesystem. On Unix the mounted file must be readable by the workload owner or group,
must not be executable or group-writable, and must not grant any access to `other`.
Modes `0400`, `0600`, `0440`, and `0640` satisfy that policy. A Kubernetes projected
Secret should use `0440` with the pod `fsGroup`; supply an optional PKCS#12 password
through bootstrap configuration such as `SecretProvider__Certificate__Password`, never
through a `{{secret:...}}` placeholder. Store selectors remain supported for hosts that
provision the operating-system certificate store; `Path` is mutually exclusive with
`Thumbprint`/`SubjectName`.

`TryGet` performs only an in-memory snapshot lookup. Configuration reload does not
contact the backend or partially refresh secrets; rotate through a controlled process
restart until an atomic live-refresh mechanism is introduced.

ApiHost resolves and validates `Runtime:Role` before adding this provider. Each alias
used by an API host must declare a non-empty `Roles` allow-list. Only placeholders whose
references authorize the active role are fetched; unowned placeholders remain in lower
configuration and cannot grant the workload identity credentials for another role.
`Migration` should normally receive only the database DDL credential, while identity,
broker, cache, storage, and signing secrets remain confined to the roles that use them.
