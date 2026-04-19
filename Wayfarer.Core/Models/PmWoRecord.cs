namespace Wayfarer.Core.Models;

public sealed class PmWoRecord
{
    public int WoNo { get; set; }
    public string? DetailUrl { get; set; }

    public string? WoCode { get; set; }
    public string? WoDate { get; set; }
    public string? WoProblem { get; set; }

    public int WoStatusNo { get; set; }
    public string? WoStatusCode { get; set; }

    public string? WoTypeCode { get; set; }

    public int EqNo { get; set; }
    public int PuNo { get; set; }

    public string? DeptCode { get; set; }
    public string? FetchedAtUtc { get; set; }
}