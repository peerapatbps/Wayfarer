using Microsoft.Extensions.Logging;
using Wayfarer.Core.Interfaces;
using Wayfarer.Core.Models;

namespace Wayfarer.Playwright.Services;

public sealed class PlaywrightPmCollector : IPmCollector
{
    private readonly ILogger<PlaywrightPmCollector> _logger;

    public PlaywrightPmCollector(ILogger<PlaywrightPmCollector> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<PmJob>> CollectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Playwright PM collector started.");

        IReadOnlyList<PmJob> jobs = new List<PmJob>
        {
            new()
            {
                JobNo = "DEMO-001",
                Title = "Demo PM Job",
                RawStatus = "Open",
                NormalizedStatus = "Not Started"
            }
        };

        return Task.FromResult(jobs);
    }
}