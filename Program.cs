var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

var urls = new[]
{
    "https://example.com",
    "https://httpbin.org/status/500"
};

var latestResults = new List<CheckResult>();
var lastUpdated = DateTimeOffset.MinValue;
var resultsLock = new object();

app.MapGet("/check", async (string url) =>
    Results.Ok(await CheckUrl(http, url)));

app.MapGet("/check-all", async () =>
    Results.Ok(await CheckAllAsync()));

app.MapGet("/status", () =>
{
    lock (resultsLock)
    {
        return Results.Ok(new
        {
            updatedAt = lastUpdated,
            results = latestResults
        });
    }
});

async Task<List<CheckResult>> CheckAllAsync()
{
    var results = new List<CheckResult>();

    foreach (var url in urls)
    {
        results.Add(await CheckUrl(http, url));
    }

    lock (resultsLock)
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

record CheckResult(string Url, int? Status, string? Error, long Ms);