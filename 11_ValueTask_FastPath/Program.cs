using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

BenchmarkRunner.Run<TitleCacheBenchmark>();

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class TitleCacheBenchmark
{
    private static readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, string> _cache = new();

    [GlobalSetup]
    public async Task Setup()
    {
        // Prime the cache with a real network call so the benchmarks
        // only measure the hot (cache-hit) path.
        var html = await _httpClient.GetStringAsync("https://example.com");
        _cache["https://example.com"] = ExtractTitle(html);
    }

    // ── The two implementations under test ──────────────────────────

    Task<string> GetTitleTaskAsync(string url)
    {
        if (_cache.TryGetValue(url, out var cached))
            return Task.FromResult(cached);

        return FetchTitleAsync(url);
    }

    ValueTask<string> GetTitleValueTaskAsync(string url)
    {
        if (_cache.TryGetValue(url, out var cached))
            return new ValueTask<string>(cached);

        return new ValueTask<string>(FetchTitleAsync(url));
    }

    async Task<string> FetchTitleAsync(string url)
    {
        var html = await _httpClient.GetStringAsync(url);
        var title = ExtractTitle(html);
        _cache[url] = title;
        return title;
    }

    static string ExtractTitle(string html)
    {
        const string open = "<title>";
        const string close = "</title>";
        var start = html.IndexOf(open, System.StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(close, System.StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < 0) return "(no title)";
        return html[(start + open.Length)..end].Trim();
    }

    // ── Benchmarks ──────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public async Task<string> Task_CacheHit()
    {
        return await GetTitleTaskAsync("https://google.com");
    }

    [Benchmark]
    public async Task<string> ValueTask_CacheHit()
    {
        return await GetTitleValueTaskAsync("https://google.com");
    }
}