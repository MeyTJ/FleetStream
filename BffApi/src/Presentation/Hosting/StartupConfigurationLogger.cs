using System.Text.Json;
using FleetStream.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FleetStream.Presentation.Hosting;

internal static class StartupConfigurationLogger
{
    private static readonly HashSet<string> RedactedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jwt:SigningKey",
        "ConnectionStrings:Redis",
        "ConnectionStrings:Kafka",
        "Redis:Configuration",
    };

    public static void LogEffectiveConfiguration(
        IConfiguration config,
        ILogger logger,
        IHostEnvironment env,
        FeaturesOptions features)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["environment"] = env.EnvironmentName,
            ["features"] = new
            {
                features.DevToken,
                features.RemoteConfig,
                features.VerboseTracing,
            },
            ["jwt"] = new
            {
                Issuer   = config["Jwt:Issuer"],
                Audience = config["Jwt:Audience"],
                JwksUri  = Redact(config["Jwt:JwksUri"]),
                SigningKey = Redact(config["Jwt:SigningKey"], fully: true),
            },
            ["redis"] = new
            {
                KeyPrefix = config["Redis:KeyPrefix"],
                Endpoints = config.GetSection("Redis:Endpoints").GetChildren()
                    .Select(e => $"{e["Host"]}:{e["Port"]}").ToArray(),
            },
            ["kafka"] = new
            {
                Brokers       = Redact(config["Kafka:Brokers"]),
                ConsumerGroup = config["Kafka:ConsumerGroup"],
                TelemetryTopic = config["Kafka:TelemetryTopic"],
                AlertTopic     = config["Kafka:AlertTopic"],
            },
            ["openTelemetry"] = new
            {
                ServiceName    = config["OpenTelemetry:ServiceName"],
                ServiceVersion = config["OpenTelemetry:ServiceVersion"],
                OtlpEndpoint   = config["OpenTelemetry:OtlpEndpoint"],
                PrometheusEnabled = config["OpenTelemetry:PrometheusEnabled"],
            },
            ["rateLimiting"] = new
            {
                GlobalPermitLimit   = config["RateLimiting:GlobalPermitLimit"],
                GlobalWindowSeconds = config["RateLimiting:GlobalWindowSeconds"],
            },
        };

        logger.LogInformation(
            "Effective configuration (redacted): {Configuration}",
            JsonSerializer.Serialize(snapshot));
    }

    private static string? Redact(string? value, bool fully = false)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (fully) return "***";
        if (value.Length <= 8) return "***";
        return value[..4] + "…" + value[^4..];
    }
}
