namespace Wayfarer.Core.Models;

public sealed class PmJob
{
    public string? JobNo { get; set; }
    public string? Title { get; set; }
    public string? RawStatus { get; set; }
    public string? NormalizedStatus { get; set; }
    public string? Assignee { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? DueAt { get; set; }
}