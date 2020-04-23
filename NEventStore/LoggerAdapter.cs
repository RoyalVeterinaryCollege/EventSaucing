using Microsoft.Extensions.Logging;
using NEventStore.Logging;
using Scalesque;

namespace EventSaucing.NEventStore {
    /// <summary>
    /// Adapts the NEventStore log interface to the serilog one
    /// </summary>
    public class LoggerAdapter : ILog {
        private readonly ILogger<LoggerAdapter> _logger;

        public bool IsVerboseEnabled => true;

        public bool IsDebugEnabled => true;

        public bool IsInfoEnabled => true;

        public bool IsWarnEnabled => true;

        public bool IsErrorEnabled => true;

        public bool IsFatalEnabled => true;

        public global::NEventStore.Logging.LogLevel LogLevel => global::NEventStore.Logging.LogLevel.Verbose;

        public LoggerAdapter(ILogger<LoggerAdapter> logger) {
            _logger = logger;
        }

        public void Verbose(string message, params object[] values) {
           _logger.LogTrace(message.format(values));
        }

        public void Debug(string message, params object[] values) {
            _logger.LogDebug(message.format(values));
        }

        public void Info(string message, params object[] values) {
            _logger.LogInformation(message.format(values));
        }

        public void Warn(string message, params object[] values) {
            _logger.LogWarning(message.format(values));
        }

        public void Error(string message, params object[] values) {
            _logger.LogError(message.format(values));
        }

        public void Fatal(string message, params object[] values) {
            _logger.LogCritical(message.format(values));
        }
    }
}
