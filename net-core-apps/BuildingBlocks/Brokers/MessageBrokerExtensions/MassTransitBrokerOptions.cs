namespace MessageBrokerExtensions;

public sealed class MassTransitBrokerOptions
{
    public const string SectionName = "Broker";

    public string Provider { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public RabbitMqOptions RabbitMq { get; set; } = new();
    public OutboxTelemetryOptions OutboxTelemetry { get; set; } = new();
}

public sealed class OutboxTelemetryOptions
{
    /// <summary>
    /// Enables periodic sampling and the readiness gate for service-owned MassTransit
    /// outbox stores. Startup rejects false when an outbox context is registered.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Frequency of the aggregate-only EF bus-outbox backlog sample.
    /// </summary>
    public int IntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Database command timeout for one aggregate-only sample.
    /// </summary>
    public int QueryTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum safe number of pending messages. Sampling reads at most this value plus
    /// one sentinel row, so a breached backlog cannot turn telemetry into an unbounded scan.
    /// </summary>
    public int MaximumPendingMessages { get; set; } = 100_000;

    /// <summary>
    /// Maximum safe age of the oldest pending message. Five minutes is the platform's
    /// current engineering safety gate; production approval still requires fault evidence.
    /// </summary>
    public int MaximumOldestMessageAgeSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum age of the last successful sample. A recent failed attempt is degraded;
    /// once this budget expires readiness fails closed.
    /// </summary>
    public int MaximumSampleAgeSeconds { get; set; } = 45;
}

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = string.Empty;
    public ushort Port { get; set; } = 5672;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";
    public ushort PrefetchCount { get; set; } = 32;
    public int ConsumerConcurrency { get; set; } = 16;
    public int HeartbeatSeconds { get; set; } = 30;
    public RabbitMqTlsOptions Tls { get; set; } = new();
}

public sealed class RabbitMqTlsOptions
{
    /// <summary>
    /// TLS is secure-by-default. Local development must opt out explicitly.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Certificate DNS name expected from the broker. Certificate policy errors are never
    /// suppressed by application configuration.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;
}
