using Wayfarer.Core.Interfaces;
using Wayfarer.Playwright.Services;
using Wayfarer.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IPmCollector, PlaywrightPmCollector>();

var host = builder.Build();
host.Run();
