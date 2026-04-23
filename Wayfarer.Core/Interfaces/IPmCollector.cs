using Wayfarer.Core.Models;

namespace Wayfarer.Core.Interfaces;

public interface IPmCollector
{
    Task<IReadOnlyList<PmJob>> CollectAsync(CancellationToken cancellationToken = default);
}