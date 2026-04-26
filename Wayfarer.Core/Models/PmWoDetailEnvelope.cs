namespace Wayfarer.Core.Models;

public sealed class PmWoDetailEnvelope
{
    public int WoNo { get; set; }
    public string? DetailUrl { get; set; }
    public string Json { get; set; } = string.Empty;
    public string? FetchedAtUtc { get; set; }
}