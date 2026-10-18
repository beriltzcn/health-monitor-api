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
    Results.Ok(await CheckUrl(http, url)));

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
            message = "Geçerli bir http/https adresi girin."
        });
    }

    lock (stateLock)
    {
        if (urls.Any(url =>
                string.Equals(url, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Conflict(new
            {
                message = "Bu adres zaten ekli."
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
            message = "Geçerli bir http/https adresi girin."
        });
    }

    lock (stateLock)
    {
        var removed = urls.RemoveAll(existing =>
            string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return Results.NotFound(new
            {
                message = "Bu adres listede yok."
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
        // Uygulama kapanırken normal.
    }
});

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
            sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        return new CheckResult(
            url,
            null,
            ex.Message,
            sw.ElapsedMilliseconds);
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
            "https://example.com",
            "https://httpbin.org/status/500"
        };
    }

    try
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json)
               ?? new List<string>();
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
record CheckResult(string Url, int? Status, string? Error, long Ms);
