using Wayfarer.Core.Interfaces;

namespace Wayfarer.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IPmCollector _pmCollector;

    public Worker(ILogger<Worker> logger, IPmCollector pmCollector)
    {
        _logger = logger;
        _pmCollector = pmCollector;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Wayfarer Worker started at: {time}", DateTimeOffset.Now);

        var jobs = await _pmCollector.CollectAsync(stoppingToken);

        _logger.LogInformation("Collected {count} PM job(s).", jobs.Count);

        foreach (var job in jobs)
        {
            _logger.LogInformation(
                "JobNo={JobNo}, Title={Title}, RawStatus={RawStatus}, NormalizedStatus={NormalizedStatus}",
                job.JobNo,
                job.Title,
                job.RawStatus,
                job.NormalizedStatus);
        }
    }
}