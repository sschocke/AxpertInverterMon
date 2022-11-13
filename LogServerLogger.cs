using ScorpioConcepts.Framework.LogServer;

public sealed class LogServerLogger : ILogger, IDisposable
{
    private readonly LogServer server;

    public LogServerLogger(Func<LogServerLoggerConfiguration> getCurrentConfig)
    {
        var config = getCurrentConfig();
        server = new LogServer(config.Port, config.Name, config.LogSize);
    }

    public void Dispose()
    {
        if (server.Active) {
            server.Stop();
            server.Join();
        }
    }

    public IDisposable BeginScope<TState>(TState state) => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            if (!server.Active)
            {
                server.Start();
            }

            server.Log($"{DateTime.Now.ToString()}: [{logLevel,-12}] - {formatter(state, exception)}");
        }
        catch
        { }
    }
}