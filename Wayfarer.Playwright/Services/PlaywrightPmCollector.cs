using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Wayfarer.Core.Interfaces;
using Wayfarer.Core.Models;
using Wayfarer.Playwright.Options;

namespace Wayfarer.Playwright.Services;

public sealed class PlaywrightPmCollector : IPmCollector, IAsyncDisposable
{
    private const string DefaultAppUrl = "https://webpm2.mwa.co.th/app";
    private const string WorkOrderListUrl = "https://webpm2.mwa.co.th/app/work-order";
    private const string TokenEndpointPath = "/sso/realms/webpm/protocol/openid-connect/token";
    private const string WoApiBaseUrl = "https://webpm2.mwa.co.th/api/api/wo";
    private const int PageSize = 1000;
    private const int SnapshotLookbackDays = 365;

    private readonly ILogger<PlaywrightPmCollector> _logger;
    private readonly PmSiteOptions _options;

    private Microsoft.Playwright.IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _sessionReady;

    public PlaywrightPmCollector(
        ILogger<PlaywrightPmCollector> logger,
        IOptions<PmSiteOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<PmWoRecord>> CollectSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoggedInSessionAsync(cancellationToken);

        if (_page is null || _context is null)
            throw new InvalidOperationException("Browser session is not ready.");

        var accessToken = await NavigateAndCaptureAccessTokenAsync(_page, WorkOrderListUrl, cancellationToken);
        var cookieHeader = await BuildCookieHeaderAsync(_context);

        return await FetchSnapshotRecordsAsync(accessToken, cookieHeader, cancellationToken);
    }

    public async Task<IReadOnlyList<PmWoDetailEnvelope>> CollectDetailPayloadsAsync(
        IReadOnlyList<PmWoRecord> snapshots,
        CancellationToken cancellationToken = default)
    {
        var payloads = new List<PmWoDetailEnvelope>();

        if (snapshots.Count == 0)
            return payloads;

        await EnsureLoggedInSessionAsync(cancellationToken);

        if (_page is null || _context is null)
            throw new InvalidOperationException("Browser session is not ready.");

        for (var index = 0; index < snapshots.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = snapshots[index];

            var detailUrl = string.IsNullOrWhiteSpace(snapshot.DetailUrl)
                ? $"https://webpm2.mwa.co.th/app/work-order/{snapshot.WoNo}"
                : snapshot.DetailUrl;

            _logger.LogInformation(
                "Opening detail page {Current}/{Total}: {Url}",
                index + 1,
                snapshots.Count,
                detailUrl);

            var accessToken = await NavigateAndCaptureAccessTokenAsync(_page, detailUrl, cancellationToken);
            var cookieHeader = await BuildCookieHeaderAsync(_context);

            var payload = await FetchDetailPayloadAsync(
                snapshot.WoNo,
                detailUrl,
                accessToken,
                cookieHeader,
                cancellationToken);

            payloads.Add(payload);

            _logger.LogInformation(
                "DETAIL PAYLOAD captured {Current}/{Total} => woNo={WoNo}, detailUrl={DetailUrl}, fetchedAtUtc={FetchedAtUtc}",
                index + 1,
                snapshots.Count,
                payload.WoNo,
                payload.DetailUrl,
                payload.FetchedAtUtc);
        }

        return payloads;
    }

    private async Task EnsureLoggedInSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionReady && _page is not null && _context is not null && _browser is not null)
            return;

        if (string.IsNullOrWhiteSpace(_options.Username))
            throw new InvalidOperationException("PmSite:Username is empty.");

        if (string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("PmSite:Password is empty.");

        var targetUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultAppUrl
            : _options.BaseUrl;

        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();
        _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless
        });

        _context ??= await _browser.NewContextAsync();
        _page ??= await _context.NewPageAsync();

        _logger.LogInformation("Navigating to: {Url}", targetUrl);

        await _page.GotoAsync(targetUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        _logger.LogInformation("Current URL: {Url}", _page.Url);

        if (_page.Url.Contains("madfed.mwa.co.th", StringComparison.OrdinalIgnoreCase))
        {
            await LoginAdfsWithRotationAsync(_page, cancellationToken);
            await WaitForWorkOrderAsync(_page);
        }
        else if (_page.Url.Contains("/app", StringComparison.OrdinalIgnoreCase))
        {
            var bodyText = await _page.Locator("body").InnerTextAsync();
            if (bodyText.Contains("พนักงาน", StringComparison.OrdinalIgnoreCase))
            {
                await ClickEmployeeLoginAsync(_page);
                await WaitForAdfsAsync(_page);
                await LoginAdfsWithRotationAsync(_page, cancellationToken);
                await WaitForWorkOrderAsync(_page);
            }
        }

        _sessionReady = true;
    }

    private async Task LoginAdfsWithRotationAsync(IPage page, CancellationToken cancellationToken)
    {
        var startingPassword = await ReadCurrentPasswordFromAppSettingsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(startingPassword))
            startingPassword = _options.Password;

        var candidates = BuildPasswordCandidates(startingPassword);

        foreach (var password in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Trying ADFS login with rotated password candidate.");

            await FillAdfsCredentialsAsync(page, _options.Username, password, cancellationToken);
            await SubmitAdfsLoginAsync(page);

            var success = await WaitForLoginSuccessAsync(page, cancellationToken);
            if (success)
            {
                var currentPasswordInFile = await ReadCurrentPasswordFromAppSettingsAsync(cancellationToken);

                if (!string.Equals(password, currentPasswordInFile, StringComparison.Ordinal))
                {
                    _logger.LogInformation("ADFS login succeeded. Working password differs from appsettings.json, updating file.");
                    await SaveWorkingPasswordToAppSettingsAsync(password, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("ADFS login succeeded. Working password already matches appsettings.json.");
                }

                return;
            }

            _logger.LogWarning("ADFS login failed for current password candidate.");
            await EnsureBackToAdfsLoginPageAsync(page, cancellationToken);
        }

        throw new InvalidOperationException("Unable to login with any rotated password candidate.");
    }

    private static List<string> BuildPasswordCandidates(string currentPassword)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) ||
            currentPassword.Length != 8 ||
            currentPassword.Any(ch => ch < '0' || ch > '9') ||
            currentPassword.Distinct().Count() != 1)
        {
            currentPassword = "11111111";
        }

        var startDigit = currentPassword[0] - '0';
        var result = new List<string>(10);

        for (var i = 0; i < 10; i++)
        {
            var digit = (startDigit + i) % 10;
            result.Add(new string((char)('0' + digit), 8));
        }

        return result;
    }

    private async Task FillAdfsCredentialsAsync(
        IPage page,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(1000, cancellationToken);

        var usernameBox =
            page.Locator("input[name='UserName']")
                .Or(page.Locator("#userNameInput"))
                .Or(page.Locator("input[type='text']")).First;

        var passwordBox =
            page.Locator("input[name='Password']")
                .Or(page.Locator("#passwordInput"))
                .Or(page.Locator("input[type='password']")).First;

        await usernameBox.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 30000,
            State = WaitForSelectorState.Visible
        });

        await passwordBox.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 30000,
            State = WaitForSelectorState.Visible
        });

        await usernameBox.FillAsync(string.Empty);
        await usernameBox.TypeAsync(username, new LocatorTypeOptions { Delay = 30 });

        await passwordBox.FillAsync(string.Empty);
        await passwordBox.TypeAsync(password, new LocatorTypeOptions { Delay = 30 });

        _logger.LogInformation("Filled username and rotated password candidate.");
    }

    private async Task<bool> WaitForLoginSuccessAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            await page.WaitForURLAsync("**webpm2.mwa.co.th/**", new PageWaitForURLOptions
            {
                Timeout = 10000
            });

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            return true;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private async Task EnsureBackToAdfsLoginPageAsync(IPage page, CancellationToken cancellationToken)
    {
        if (page.Url.Contains("madfed.mwa.co.th", StringComparison.OrdinalIgnoreCase))
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            return;
        }

        await page.GotoAsync("https://madfed.mwa.co.th/adfs/ls/?wa=wsignin1.0", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30000
        });

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(500, cancellationToken);
    }

    private async Task SaveWorkingPasswordToAppSettingsAsync(
        string newPassword,
        CancellationToken cancellationToken)
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        _logger.LogInformation("Writing working password to appsettings path: {Path}", appSettingsPath);

        if (!File.Exists(appSettingsPath))
            throw new FileNotFoundException("appsettings.json not found.", appSettingsPath);

        var json = await File.ReadAllTextAsync(appSettingsPath, cancellationToken);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Unable to parse appsettings.json.");

        if (root["PmSite"] is not JsonObject pmSite)
        {
            pmSite = new JsonObject();
            root["PmSite"] = pmSite;
        }

        pmSite["Password"] = newPassword;

        var updatedJson = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(appSettingsPath, updatedJson, cancellationToken);
    }

    private async Task ClickEmployeeLoginAsync(IPage page)
    {
        var employeeContainer = page.Locator("xpath=//*[contains(normalize-space(.), 'พนักงาน')]").First;
        var innerClickable = employeeContainer.Locator("a, button, div[role='button']");
        var innerCount = await innerClickable.CountAsync();

        _logger.LogInformation("Employee container inner clickable count = {Count}", innerCount);

        if (innerCount <= 0)
            throw new InvalidOperationException("Unable to click employee login entry.");

        await innerClickable.First.ClickAsync(new LocatorClickOptions
        {
            Timeout = 10000,
            Force = true
        });

        _logger.LogInformation("Clicked inner clickable inside employee container.");
    }

    private async Task WaitForAdfsAsync(IPage page)
    {
        _logger.LogInformation("Waiting for redirect to ADFS...");

        await page.WaitForURLAsync("**madfed.mwa.co.th/**", new PageWaitForURLOptions
        {
            Timeout = 60000
        });

        _logger.LogInformation("Redirected to ADFS: {Url}", page.Url);
    }

    private async Task SubmitAdfsLoginAsync(IPage page)
    {
        await page.Locator("#submitButton, input[type='submit'], button[type='submit']").First.ClickAsync(new LocatorClickOptions
        {
            Timeout = 10000,
            Force = true
        });

        _logger.LogInformation("Clicked submit input.");
    }

    private async Task WaitForWorkOrderAsync(IPage page)
    {
        _logger.LogInformation("Waiting for redirect back to WEBPM...");

        await page.WaitForURLAsync("**webpm2.mwa.co.th/**", new PageWaitForURLOptions
        {
            Timeout = 60000
        });

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await TryWaitForNetworkIdleAsync(page, "post-login work-order landing page");

        _logger.LogInformation("Redirected after login to: {Url}", page.Url);
    }

    private async Task<string> NavigateAndCaptureAccessTokenAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Navigating and capturing token: {Url}", url);

        var response = await page.RunAndWaitForResponseAsync(
            async () =>
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                await TryWaitForNetworkIdleAsync(page, url);
            },
            r => r.Url.Contains(TokenEndpointPath, StringComparison.OrdinalIgnoreCase)
                 && r.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase),
            new PageRunAndWaitForResponseOptions
            {
                Timeout = 60000
            });

        cancellationToken.ThrowIfCancellationRequested();

        var body = await response.TextAsync();

        var tokenPayload = JsonSerializer.Deserialize<TokenResponse>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (string.IsNullOrWhiteSpace(tokenPayload?.AccessToken))
            throw new InvalidOperationException("Token endpoint returned no access_token.");

        _logger.LogInformation("Access token acquired successfully.");
        return tokenPayload.AccessToken;
    }

    private async Task TryWaitForNetworkIdleAsync(IPage page, string contextLabel)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
            {
                Timeout = 10000
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "NetworkIdle timeout while loading {ContextLabel}. Continuing because DOM content is already available.",
                contextLabel);
        }
    }

    private async Task<string> BuildCookieHeaderAsync(IBrowserContext context)
    {
        var cookies = await context.CookiesAsync();
        var webpmCookies = cookies
            .Where(c => c.Domain.Contains("webpm2.mwa.co.th", StringComparison.OrdinalIgnoreCase)
                     || c.Domain.Contains("madfed.mwa.co.th", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{c.Name}={c.Value}")
            .ToList();

        return string.Join("; ", webpmCookies);
    }

    private async Task<IReadOnlyList<PmWoRecord>> FetchSnapshotRecordsAsync(
        string accessToken,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var http = CreateApiClient(accessToken, cookieHeader);

        var records = new List<PmWoRecord>();
        var cutoffUtcDate = DateTime.UtcNow.Date.AddDays(-SnapshotLookbackDays);
        var offset = 0;
        var fetchedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        _logger.LogInformation("WO cutoff UTC date = {CutoffDate:yyyy-MM-dd}", cutoffUtcDate);

        while (true)
        {
            var url = BuildWoApiUrl(offset, PageSize);
            _logger.LogInformation("WO API URL: {Url}", url);

            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var payload = JsonSerializer.Deserialize<WoListResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Data is null || payload.Data.Count == 0)
                break;

            var shouldStop = false;

            foreach (var item in payload.Data)
            {
                if (!DateTimeOffset.TryParse(item.WoDate, out var woDateOffset))
                    continue;

                var woUtcDate = woDateOffset.UtcDateTime.Date;

                if (woUtcDate < cutoffUtcDate)
                {
                    shouldStop = true;
                    break;
                }

                var record = new PmWoRecord
                {
                    WoNo = item.WoNo,
                    DetailUrl = $"https://webpm2.mwa.co.th/app/work-order/{item.WoNo}",
                    WoCode = item.WoCode ?? string.Empty,
                    WoDate = woUtcDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    WoProblem = item.WoProblem ?? string.Empty,
                    WoStatusNo = item.WoStatusNo,
                    WoStatusCode = item.Status?.WoStatusCode ?? string.Empty,
                    WoTypeCode = item.Wotype?.WoTypeCode ?? string.Empty,
                    EqNo = item.Eq?.EqNo ?? 0,
                    PuNo = item.Pu?.PuNo ?? 0,
                    DeptCode = item.MaintenanceDept?.DeptCode ?? string.Empty,
                    FetchedAtUtc = fetchedAtUtc
                };

                records.Add(record);

                _logger.LogInformation(
                    "DEBUG SNAPSHOT => woNo={WoNo}, detailUrl={DetailUrl}, woCode={WoCode}, woDate={WoDate}, woProblem={WoProblem}, woStatusNo={WoStatusNo}, woStatusCode={WoStatusCode}, woTypeCode={WoTypeCode}, eqNo={EqNo}, puNo={PuNo}, deptCode={DeptCode}",
                    record.WoNo,
                    record.DetailUrl,
                    record.WoCode,
                    record.WoDate,
                    record.WoProblem,
                    record.WoStatusNo,
                    record.WoStatusCode,
                    record.WoTypeCode,
                    record.EqNo,
                    record.PuNo,
                    record.DeptCode);
            }

            if (shouldStop)
                break;

            offset += PageSize;

            if (payload.PageInfo is not null && offset >= payload.PageInfo.Total)
                break;
        }

        _logger.LogInformation(
            "Collected {Count} PM work order record(s) within the last {Days} days.",
            records.Count,
            SnapshotLookbackDays);

        return records;
    }

    private async Task<PmWoDetailEnvelope> FetchDetailPayloadAsync(
        int woNo,
        string detailUrl,
        string accessToken,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var http = CreateApiClient(accessToken, cookieHeader);

        var apiUrl = $"{WoApiBaseUrl}/{woNo}";
        _logger.LogInformation("WO DETAIL API URL: {Url}", apiUrl);

        using var response = await http.GetAsync(apiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return new PmWoDetailEnvelope
        {
            WoNo = woNo,
            DetailUrl = detailUrl,
            Json = json,
            FetchedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
    }

    private static HttpClient CreateApiClient(string accessToken, string cookieHeader)
    {
        var http = new HttpClient();

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(cookieHeader))
            http.DefaultRequestHeaders.Add("Cookie", cookieHeader);

        return http;
    }

    private static string BuildWoApiUrl(int offset, int pageSize)
    {
        return $"{WoApiBaseUrl}" +
               $"?pageSize={pageSize}" +
               $"&offset={offset}" +
               $"&search=" +
               $"&searchEQ=" +
               $"&orderBy=wodate" +
               $"&orderDirection=desc" +
               $"&myOrder=true" +
               $"&siteNo=[103]";
    }

    public async ValueTask DisposeAsync()
    {
        if (_page is not null)
            await _page.CloseAsync();

        if (_context is not null)
            await _context.DisposeAsync();

        if (_browser is not null)
            await _browser.DisposeAsync();

        _playwright?.Dispose();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class WoListResponse
    {
        public List<WoApiItem> Data { get; set; } = new();
        public WoPageInfo? PageInfo { get; set; }
    }

    private sealed class WoPageInfo
    {
        public int Total { get; set; }
    }

    private sealed class WoApiItem
    {
        public int WoNo { get; set; }
        public string? WoCode { get; set; }
        public string? WoDate { get; set; }
        public string? WoProblem { get; set; }
        public int WoStatusNo { get; set; }
        public WoTypeInfo? Wotype { get; set; }
        public EqInfo? Eq { get; set; }
        public PuInfo? Pu { get; set; }
        public StatusInfo? Status { get; set; }

        [JsonPropertyName("maintenance_dept")]
        public MaintenanceDeptInfo? MaintenanceDept { get; set; }
    }

    private sealed class WoTypeInfo
    {
        public string? WoTypeCode { get; set; }
    }

    private sealed class EqInfo
    {
        public int EqNo { get; set; }
    }

    private sealed class PuInfo
    {
        public int PuNo { get; set; }
    }

    private sealed class StatusInfo
    {
        public string? WoStatusCode { get; set; }
        public string? WoStatusName { get; set; }
    }

    private sealed class MaintenanceDeptInfo
    {
        public string? DeptCode { get; set; }
    }

    private async Task<string?> ReadCurrentPasswordFromAppSettingsAsync(CancellationToken cancellationToken)
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        _logger.LogInformation("Reading password from appsettings path: {Path}", appSettingsPath);

        if (!File.Exists(appSettingsPath))
            return null;

        var json = await File.ReadAllTextAsync(appSettingsPath, cancellationToken);
        var root = JsonNode.Parse(json)?.AsObject();

        return root?["PmSite"]?["Password"]?.GetValue<string>();
    }
}
