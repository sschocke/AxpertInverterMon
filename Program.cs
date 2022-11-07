using InverterMon;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
    })
    .ConfigureLogging(logging => 
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddLogServerLogger(configure => {
            configure.Name = "Axpert Inverter Monitor Log";
        });
    })
    .Build();

await host.RunAsync();
