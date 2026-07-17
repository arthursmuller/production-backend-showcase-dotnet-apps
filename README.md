# Comment, Feed, and Following services

This workspace contains the three retained .NET services and the shared projects they require:

- `Services/Comment`
- `Services/Feed`
- `Services/Following`
- `BuildingBlocks`

Each service follows the same dependency direction:

```text
Api -> Application
Api -> Infraestructure
Infraestructure -> Application -> Domain
Infraestructure -> Domain
```

Application owns commands, DTOs, integration-message contracts, and persistence/provider interfaces. Infrastructure implements those interfaces. APIs own HTTP routes, queries that remain API-specific, and RabbitMQ consumers under `AppInterfaces/Queues`.

## Build

From `net-core-apps`:

```powershell
dotnet restore net-core-apps.sln
dotnet build net-core-apps.sln
```

The projects target .NET 10.

## Local infrastructure

The Compose configuration starts MariaDB, RabbitMQ, MinIO, and the three APIs:

```powershell
docker compose up --build
```

The service ports are:

| Service | Port |
|---|---:|
| Feed | 5006 |
| Comment | 5014 |
| Following | 5016 |

## API startup

All three APIs use the shared host configuration:

```csharp
ApiBuilder.New(new ApiOptions
{
    AppName = "Comment",
    WithApplication = true,
    WithInfra = true,
    WithCqrs = new(),
    WithQueues = MessageBrokers.RabbitMQ,
});
```

`WithApplication` discovers and invokes `ConfigureApplication(IServiceCollection, IConfiguration)` and includes that assembly in CQRS registration.

`WithQueues = MessageBrokers.RabbitMQ` configures the RabbitMQ connection, discovers consumers in the API's `.AppInterfaces.Queues` namespace, registers them as `IMessageBroker` singletons, and subscribes them after the application is built. Consumer constructors receive only `IServiceProvider`, retry count, and `ILogger<T>`; exchange, routing key, and queue values come directly from `Topology` in each base-constructor call.

## Publishing

Domain-aware publishing belongs in Application. For example, Feed's `MessagePublisher` composes the transport-neutral `IMessageBrokerPublisher` with Feed message contracts and topology. Infrastructure only supplies the concrete RabbitMQ transport through `RabbitMqProvider : MessageBrokerRabbitMQPublisher`.

## Configuration

The APIs read runtime settings from environment variables. The main settings are:

- `SQL_CONNECTION_STRING`
- `RMQ_CONNECTION`
- `MINIO_CONNECTION` for Comment storage
- identity/client credential settings used by retained provider adapters

Per-project `Properties/launchSettings.json` files contain local development profiles.
