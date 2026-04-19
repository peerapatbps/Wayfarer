using Wayfarer.Core.Models;

namespace Wayfarer.Core.Interfaces;

public interface IPmCollector
{
    Task<IReadOnlyList<PmWoRecord>> CollectSnapshotsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PmWoDetailEnvelope>> CollectDetailPayloadsAsync(
        IReadOnlyList<PmWoRecord> snapshots,
        CancellationToken cancellationToken = default);
}