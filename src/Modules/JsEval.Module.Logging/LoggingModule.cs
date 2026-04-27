using Cocoar.JsEval;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.Logging;
#pragma warning restore CA1716

#pragma warning disable CA1848 // LoggerMessage delegates not required for JS-facing module logging
#pragma warning disable CA2254 // Message template varies by design — JS scripts provide dynamic log messages
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
#pragma warning restore CA2254
#pragma warning restore CA1848
