var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddRabbitMQClient("rabbitmq");

var host = builder.Build();


await host.RunAsync();
