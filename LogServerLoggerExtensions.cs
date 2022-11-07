using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Configuration;

public static class LogServerLoggerExtensions
{
    public static ILoggingBuilder AddLogServerLogger(this ILoggingBuilder builder)
    {
        builder.AddConfiguration();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LogServerProvider>());

        LoggerProviderOptions.RegisterProviderOptions<LogServerLoggerConfiguration, LogServerProvider>(builder.Services);

        return builder;
    }

    public static ILoggingBuilder AddLogServerLogger(
        this ILoggingBuilder builder,
        Action<LogServerLoggerConfiguration> configure)
    {
        builder.AddLogServerLogger();
        builder.Services.Configure(configure);

        return builder;
    }
}