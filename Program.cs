using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

var dataFile = Path.Combine(app.Environment.ContentRootPath, "urls.json");
var urls = LoadUrls(dataFile);

var latestResults = new List<CheckResult>();
var lastUpdated = DateTimeOffset.MinValue;
var stateLock = new object();

app.MapGet("/check", async (string url) =>
{
    var result = await CheckUrl(http, url);
    RememberResult(result);
    return Results.Ok(result);
});

app.MapGet("/check-all", async () =>
    Results.Ok(await CheckAllAsync()));

app.MapGet("/status", () =>
{
    lock (stateLock)
    {
        return Results.Ok(new
        {
            updatedAt = lastUpdated,
            results = latestResults
        });
    }
});

app.MapGet("/api/urls", () =>
{
    lock (stateLock)
    {
        return Results.Ok(urls.ToArray());
    }
});

app.MapPost("/api/urls", (AddUrlRequest request) =>
{
    if (!TryNormalizeUrl(request.Url, out var normalized))
    {
        return Results.BadRequest(new
        {
            message = "Enter a valid HTTP or HTTPS URL."
        });
    }

    lock (stateLock)
    {
        if (urls.Any(url =>
                string.Equals(url, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new
            {
                message = "This URL is already being monitored."
            });
        }

        urls.Add(normalized);
        SaveUrls(dataFile, urls);
    }

    return Results.Created("/api/urls", new { url = normalized });
});

app.MapDelete("/api/urls", (string url) =>
{
    if (!TryNormalizeUrl(url, out var normalized))
    {
        return Results.BadRequest(new
        {
            message = "Enter a valid HTTP or HTTPS URL."
        });
    }

    lock (stateLock)
    {
        var removed = urls.RemoveAll(existing =>
            TryNormalizeUrl(existing, out var normalizedExisting) &&
            string.Equals(
                normalizedExisting,
                normalized,
                StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return Results.NotFound(new
            {
                message = "This URL is not in the list."
            });
        }

        latestResults = latestResults
            .Where(result =>
                !string.Equals(result.Url, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SaveUrls(dataFile, urls);
    }

    return Results.NoContent();
});

async Task<List<CheckResult>> CheckAllAsync()
{
    string[] urlsToCheck;

    lock (stateLock)
    {
        urlsToCheck = urls.ToArray();
    }

    var results = new List<CheckResult>();

    foreach (var url in urlsToCheck)
    {
        results.Add(await CheckUrl(http, url));
    }

    lock (stateLock)
    {
        latestResults = results;
        lastUpdated = DateTimeOffset.Now;
    }

    return results;
}

var appStopping = app.Lifetime.ApplicationStopping;

_ = Task.Run(async () =>
{
    try
    {
        await CheckAllAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(appStopping))
        {
            await CheckAllAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // Normal during application shutdown.
    }
});

void RememberResult(CheckResult result)
{
    lock (stateLock)
    {
        if (!urls.Any(url =>
        string.Equals(url, result.Url, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var index = latestResults.FindIndex(existing =>
            string.Equals(existing.Url, result.Url, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            latestResults[index] = result;
        }
        else
        {
            latestResults.Add(result);
        }

        lastUpdated = DateTimeOffset.Now;
    }
}

app.Run();

static async Task<CheckResult> CheckUrl(HttpClient http, string url)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        var response = await http.GetAsync(url);

        return new CheckResult(
            url,
            (int)response.StatusCode,
            null,
            sw.ElapsedMilliseconds,
            DateTimeOffset.UtcNow);
    }
    catch (Exception ex)
    {
        return new CheckResult(
            url,
            null,
            ex.Message,
            sw.ElapsedMilliseconds,
            DateTimeOffset.UtcNow);
    }
}

static bool TryNormalizeUrl(string? value, out string normalized)
{
    normalized = string.Empty;

    if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
    {
        return false;
    }

    normalized = uri.ToString();
    return true;
}

static List<string> LoadUrls(string path)
{
    if (!File.Exists(path))
    {
        return new List<string>
        {
            "https://example.com/",
            "https://httpbin.org/status/500"
        };
    }

    try
    {
        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<List<string>>(json)
                     ?? new List<string>();

        var normalizedUrls = new List<string>();

        foreach (var url in loaded)
        {
            if (TryNormalizeUrl(url, out var normalized) &&
                !normalizedUrls.Any(existing =>
                    string.Equals(
                        existing,
                        normalized,
                        StringComparison.OrdinalIgnoreCase)))
            {
                normalizedUrls.Add(normalized);
            }
        }

        return normalizedUrls;
    }
    catch
    {
        return new List<string>();
    }
}

static void SaveUrls(string path, List<string> urls)
{
    var json = JsonSerializer.Serialize(urls, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    File.WriteAllText(path, json);
}

record AddUrlRequest(string Url);
record CheckResult(string Url, int? Status, string? Error, long Ms, DateTimeOffset CheckedAt);
