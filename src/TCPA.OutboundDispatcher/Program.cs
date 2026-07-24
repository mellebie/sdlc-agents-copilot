var builder = Host.CreateApplicationBuilder(args);

// Hosted services will be added in subsequent tasks
var host = builder.Build();
host.Run();
