package config

import (
	"strings"

	"github.com/sirupsen/logrus"
)

func ParseLogLevel(level string) logrus.Level {
	switch strings.ToLower(strings.TrimSpace(level)) {
	case "trace":
		return logrus.TraceLevel
	case "debug":
		return logrus.DebugLevel
	case "warn", "warning":
		return logrus.WarnLevel
	case "error":
		return logrus.ErrorLevel
	case "fatal":
		return logrus.FatalLevel
	case "panic":
		return logrus.PanicLevel
	default:
		return logrus.InfoLevel
	}
}
