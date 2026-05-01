using Wayfarer.Core.Interfaces;
using Wayfarer.Worker.Config;

namespace Wayfarer.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IPmCollector _pmCollector;
    private readonly SqlitePmSnapshotStore _store;
    private readonly IHostApplicationLifetime _appLifetime;

    public Worker(
        ILogger<Worker> logger,
        IPmCollector pmCollector,
        SqlitePmSnapshotStore store,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _pmCollector = pmCollector;
        _store = store;
        _appLifetime = appLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Wayfarer Worker started at: {time}", DateTimeOffset.Now);

            var snapshots = await _pmCollector.CollectSnapshotsAsync(stoppingToken);
            await _store.SaveSnapshotsAsync(snapshots, stoppingToken);

            var indexRows = await _store.LoadIndexRecordsAsync(stoppingToken);
            var detailPayloads = await _pmCollector.CollectDetailPayloadsAsync(indexRows, stoppingToken);

            await _store.SaveDetailPayloadsAsync(detailPayloads, stoppingToken);

            _logger.LogInformation("Wayfarer Worker finished.");
        }
        finally
        {
            // This worker is intended to be a one-shot collection job.
            // Stop the generic host so the process exits and the scheduler can continue.
            _appLifetime.StopApplication();
        }
    }
}
