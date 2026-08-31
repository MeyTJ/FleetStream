using Confluent.Kafka;
using FleetStream.Infrastructure.Options;

namespace FleetStream.Infrastructure.Messaging;

public static class KafkaClientConfig
{
    public static void ApplySecurity(ClientConfig config, KafkaOptions opts)
    {
        if (opts.TlsEnabled)
        {
            config.SecurityProtocol = string.IsNullOrEmpty(opts.SaslMechanism)
                ? SecurityProtocol.Ssl
                : SecurityProtocol.SaslSsl;
            if (!string.IsNullOrWhiteSpace(opts.CaCertPath))
                config.SslCaLocation = opts.CaCertPath;
            config.EnableSslCertificateVerification = !opts.TlsSkipVerify;
        }
        else if (!string.IsNullOrEmpty(opts.SaslMechanism))
        {
            config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
        }

        if (string.IsNullOrEmpty(opts.SaslMechanism))
            return;

        config.SaslMechanism = opts.SaslMechanism switch
        {
            "PLAIN"         => SaslMechanism.Plain,
            "SCRAM-SHA-256" => SaslMechanism.ScramSha256,
            "SCRAM-SHA-512" => SaslMechanism.ScramSha512,
            _               => throw new InvalidOperationException($"Unsupported Kafka SASL mechanism: {opts.SaslMechanism}"),
        };
        config.SaslUsername = opts.SaslUsername;
        config.SaslPassword = opts.SaslPassword;
    }
}
