using Microsoft.Extensions.Logging;
using SharpMP4.Common;

namespace CameraRecorder
{
    public class Mp4Logger : IMp4Logger
    {
        private readonly ILogger<Mp4Logger> _logger;

        public bool IsErrorEnabled { get; set; } = true;
        public bool IsWarningEnabled { get; set; } = true;
        public bool IsInfoEnabled { get; set; } = true;
        public bool IsDebugEnabled { get; set; } = true;
        public bool IsTraceEnabled { get; set; } = true;


        public Mp4Logger(ILogger<Mp4Logger> logger)
        {
            _logger = logger;
        }
        public void LogDebug(string debug)
        {
            _logger.LogDebug(debug);
        }

        public void LogError(string error)
        {
            _logger.LogError(error);
        }

        public void LogInfo(string info)
        {
            _logger.LogInformation(info);
        }

        public void LogTrace(string trace)
        {
            _logger.LogTrace(trace);
        }

        public void LogWarning(string warning)
        {
            _logger.LogWarning(warning);
        }
    }
}
