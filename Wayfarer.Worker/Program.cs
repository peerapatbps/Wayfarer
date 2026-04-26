using Wayfarer.Core.Interfaces;
using Wayfarer.Playwright.Options;
using Wayfarer.Playwright.Services;
using Wayfarer.Worker;
using Wayfarer.Worker.Config;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<PmSiteOptions>(
    builder.Configuration.GetSection("PmSite"));

builder.Services.AddSingleton<IPmCollector, PlaywrightPmCollector>();
builder.Services.AddSingleton<SqlitePmSnapshotStore>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();