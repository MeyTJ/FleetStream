package observability

import (
	"fmt"
	"os"

	"github.com/fleetstream/ingress-gateway/pkg/config"
	"github.com/sirupsen/logrus"
)

func NewLogger(cfg config.LoggingConfig) (*logrus.Logger, error) {
	logger := logrus.New()

	switch cfg.Format {
	case "text":
		logger.SetFormatter(&logrus.TextFormatter{
			TimestampFormat: "2006-01-02T15:04:05.000Z07:00",
			FullTimestamp:   true,
		})
	default:
		logger.SetFormatter(&logrus.JSONFormatter{
			TimestampFormat: "2006-01-02T15:04:05.000Z07:00",
		})
	}

	switch cfg.Output {
	case "stderr":
		logger.SetOutput(os.Stderr)
	case "file":
		if cfg.FilePath == "" {
			return nil, fmt.Errorf("logging.file_path is required when output is file")
		}
		f, err := os.OpenFile(cfg.FilePath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644)
		if err != nil {
			return nil, fmt.Errorf("open log file: %w", err)
		}
		logger.SetOutput(f)
	default:
		logger.SetOutput(os.Stdout)
	}

	level, err := logrus.ParseLevel(cfg.Level)
	if err != nil {
		level = logrus.InfoLevel
	}
	logger.SetLevel(level)
	logger.AddHook(ContextHook{})

	return logger, nil
}
