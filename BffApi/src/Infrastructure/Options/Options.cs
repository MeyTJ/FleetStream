using System.ComponentModel.DataAnnotations;

namespace FleetStream.Infrastructure.Options;

/// <summary>Strongly-typed configuration for the Redis connection.</summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required, MinLength(1)]
    public List<RedisEndpoint> Endpoints { get; set; } = new() { new() { Host = "localhost", Port = 6379 } };

    [Range(100, 60_000)]
    public int ConnectTimeoutMs { get; set; } = 5_000;

    [Range(100, 60_000)]
    public int SyncTimeoutMs { get; set; } = 5_000;

    public bool AbortOnConnectFail { get; set; } = false;

    [Required]
    public string KeyPrefix { get; set; } = "fleetstream";
}

public sealed class RedisEndpoint
{
    [Required] public string Host { get; set; } = "localhost";
    [Range(1, 65535)] public int Port { get; set; } = 6379;
}

/// <summary>Strongly-typed configuration for the Kafka consumers.</summary>
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    [Required, MinLength(1)]
    public string Brokers { get; set; } = "localhost:9092";

    [Required, MinLength(1)]
    public string ConsumerGroup { get; set; } = "fleetstream-bff";

    [Required, MinLength(1)]
    public string TelemetryTopic { get; set; } = "fleet.telemetry.processed";

    [Required, MinLength(1)]
    public string AlertTopic { get; set; } = "fleet.alerts";

    [Required, MinLength(1)]
    public string DlqTopic { get; set; } = "fleet.bff.dlq";

    [Range(1, 10_000)]
    public int CommitBatchSize { get; set; } = 100;

    [Range(100, 60_000)]
    public int CommitIntervalMs { get; set; } = 5_000;

    public bool TlsEnabled { get; set; }

    public string CaCertPath { get; set; } = string.Empty;

    public bool TlsSkipVerify { get; set; }

    public string SaslMechanism { get; set; } = string.Empty;

    public string SaslUsername { get; set; } = string.Empty;

    public string SaslPassword { get; set; } = string.Empty;
}

/// <summary>Strongly-typed configuration for JWT bearer auth.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(1)]
    public string Issuer { get; set; } = "https://auth.fleetstream.example.com";

    [Required, MinLength(1)]
    public string Audience { get; set; } = "fleetstream-bff";

    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;

    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    public string JwksUri { get; set; } = string.Empty;
}

/// <summary>Strongly-typed configuration for SignalR.</summary>
public sealed class SignalROptions
{
    public const string SectionName = "SignalR";

    [Range(1024, 1_048_576)]
    public int MaximumReceiveMessageSize { get; set; } = 32_768;

    [Range(5, 120)]
    public int KeepAliveSeconds { get; set; } = 15;

    [Range(15, 600)]
    public int ClientTimeoutSeconds { get; set; } = 60;
}

/// <summary>Strongly-typed configuration for the rate limiter.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int GlobalPermitLimit { get; set; } = 1000;

    [Range(1, 3600)]
    public int GlobalWindowSeconds { get; set; } = 60;
}

/// <summary>Strongly-typed configuration for OpenTelemetry.</summary>
public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    [Required, MinLength(1)]
    public string ServiceName { get; set; } = "fleetstream-bff";

    [Required, MinLength(1)]
    public string ServiceVersion { get; set; } = "1.0.0";

    public string OtlpEndpoint { get; set; } = string.Empty;

    public bool PrometheusEnabled { get; set; } = true;
}

/// <summary>Feature flags per 06-configuration.md §6.5.</summary>
public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    public bool DevToken { get; set; } = true;

    public bool RemoteConfig { get; set; } = false;

    public bool VerboseTracing { get; set; } = false;
}
