using Cocoar.JsEval;
using Microsoft.Extensions.Logging;

namespace Cocoar.JsEval.Module.Logging;

public class LoggingModule(ILogger<LoggingModule> logger) : IJsModule
{
    private readonly ILogger _logger = logger;

    public void Log(LogLevel logLevel, int eventId, string message, params object?[] args)
    {
        _logger.Log(logLevel, eventId, message, args);
    }

    public void Log(LogLevel logLevel, string message, params object?[] args)
    {
        _logger.Log(logLevel, message, args);
    }

    public void Info(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Information, eventId, message, args);
    }

    public void Info(string message, params object?[] args)
    {
        Log(LogLevel.Information, message, args);
    }

    public void Critical(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Critical, eventId, message, args);
    }

    public void Critical(string message, params object?[] args)
    {
        Log(LogLevel.Critical, message, args);
    }

    public void Debug(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Debug, eventId, message, args);
    }

    public void Debug(string message, params object?[] args)
    {
        Log(LogLevel.Debug, message, args);
    }

    public void Error(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Error, eventId, message, args);
    }

    public void Error(string message, params object?[] args)
    {
        Log(LogLevel.Error, message, args);
    }

    public void Trace(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Trace, eventId, message, args);
    }

    public void Trace(string message, params object?[] args)
    {
        Log(LogLevel.Trace, message, args);
    }

    public void Warning(int eventId, string message, params object?[] args)
    {
        Log(LogLevel.Warning, eventId, message, args);
    }

    public void Warning(string message, params object?[] args)
    {
        Log(LogLevel.Warning, message, args);
    }
}
