package kafkasecurity

import (
	"crypto/tls"
	"crypto/x509"
	"fmt"
	"os"
	"strconv"

	"github.com/IBM/sarama"
)

type Config struct {
	TLSEnabled    bool
	CACertPath    string
	TLSSkipVerify bool
	SASLMechanism string
	SASLUsername  string
	SASLPassword  string
}

func FromEnv() Config {
	return Config{
		TLSEnabled:    envBool("KAFKA_TLS_ENABLED"),
		CACertPath:    os.Getenv("KAFKA_CA_CERT_PATH"),
		TLSSkipVerify: envBool("KAFKA_TLS_INSECURE_SKIP_VERIFY"),
		SASLMechanism: os.Getenv("KAFKA_SASL_MECHANISM"),
		SASLUsername:  os.Getenv("KAFKA_SASL_USERNAME"),
		SASLPassword:  os.Getenv("KAFKA_SASL_PASSWORD"),
	}
}

func Apply(cfg *sarama.Config, sec Config) error {
	if !sec.TLSEnabled && sec.SASLMechanism == "" {
		return nil
	}

	if sec.TLSEnabled {
		tlsCfg := &tls.Config{MinVersion: tls.VersionTLS12}
		if sec.TLSSkipVerify {
			tlsCfg.InsecureSkipVerify = true
		}
		if sec.CACertPath != "" {
			ca, err := os.ReadFile(sec.CACertPath)
			if err != nil {
				return fmt.Errorf("read KAFKA_CA_CERT_PATH: %w", err)
			}
			pool := x509.NewCertPool()
			if !pool.AppendCertsFromPEM(ca) {
				return fmt.Errorf("invalid CA cert at %s", sec.CACertPath)
			}
			tlsCfg.RootCAs = pool
		}
		cfg.Net.TLS.Enable = true
		cfg.Net.TLS.Config = tlsCfg
	}

	if sec.SASLMechanism == "" {
		return nil
	}

	cfg.Net.SASL.Enable = true
	cfg.Net.SASL.User = sec.SASLUsername
	cfg.Net.SASL.Password = sec.SASLPassword

	switch sec.SASLMechanism {
	case "PLAIN":
		cfg.Net.SASL.Mechanism = sarama.SASLTypePlaintext
	case "SCRAM-SHA-256":
		cfg.Net.SASL.Mechanism = sarama.SASLTypeSCRAMSHA256
	case "SCRAM-SHA-512":
		cfg.Net.SASL.Mechanism = sarama.SASLTypeSCRAMSHA512
	default:
		return fmt.Errorf("unsupported KAFKA_SASL_MECHANISM: %s", sec.SASLMechanism)
	}

	return nil
}

func envBool(key string) bool {
	v, ok := os.LookupEnv(key)
	if !ok || v == "" {
		return false
	}
	b, err := strconv.ParseBool(v)
	return err == nil && b
}
