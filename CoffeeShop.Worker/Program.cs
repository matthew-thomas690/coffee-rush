using CoffeeShop.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<BaristaWorker>();

var host = builder.Build();
await host.RunAsync();
