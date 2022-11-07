using System.Runtime.Versioning;
using Microsoft.Extensions.Options;

[UnsupportedOSPlatform("browser")]
[ProviderAlias("LogServer")]
public class LogServerProvider : ILoggerProvider
{
    private bool disposedValue;
    private readonly IDisposable _onChangeToken;

    private LogServerLoggerConfiguration _currentConfig;
    private LogServerLogger? _logger;

    public LogServerProvider(IOptionsMonitor<LogServerLoggerConfiguration> config) {
        _currentConfig = config.CurrentValue;
        _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (_logger == null)
        {
            _logger = new LogServerLogger(() => _currentConfig);
        }

        return _logger;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _logger?.Dispose();
                _onChangeToken.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}