#nullable enable
using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace Torch.API.WebAPI.Plugins;

internal class NLogLogger : ILogger
{
    private readonly NLog.ILogger _logger;

    public NLogLogger(NLog.ILogger logger)
    {
        _logger = logger;
    }

    public void LogDebug(string data)
    {
        _logger.Debug(data);
    }

    public void LogVerbose(string data)
    {
        _logger.Trace(data);
    }

    public void LogInformation(string data)
    {
        _logger.Info(data);
    }

    public void LogMinimal(string data)
    {
        _logger.Debug(data);
    }

    public void LogWarning(string data)
    {
        _logger.Warn(data);
    }

    public void LogError(string data)
    {
        _logger.Error(data);
    }

    public void LogInformationSummary(string data)
    {
        _logger.Info(data);
    }

    public void Log(LogLevel level, string data)
    {
        _logger.Log(ToNLogLevel(level), data);
    }

    private static NLog.LogLevel ToNLogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => NLog.LogLevel.Debug,
            LogLevel.Verbose => NLog.LogLevel.Trace,
            LogLevel.Information => NLog.LogLevel.Info,
            LogLevel.Minimal => NLog.LogLevel.Debug,
            LogLevel.Warning => NLog.LogLevel.Warn,
            LogLevel.Error => NLog.LogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };
    }

    public Task LogAsync(LogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public void Log(ILogMessage message)
    {
        _logger.Log(ToNLogLevel(message.Level), message.FormatWithCode);
    }

    public Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}