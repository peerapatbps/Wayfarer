namespace Wayfarer.Playwright.Options;

public sealed class PmSiteOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Headless { get; set; } = true;
}