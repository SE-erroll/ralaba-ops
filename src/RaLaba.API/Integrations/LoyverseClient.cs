using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RaLaba.API.Integrations;

public sealed class LoyverseOptions
{
    public string AccessToken { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset StartAt { get; set; } = new(2026, 11, 4, 0, 0, 0, TimeSpan.FromHours(8));
    public int IntervalSeconds { get; set; } = 300;
}

public sealed class LoyverseException(string message) : Exception(message);

// The token is used only here, with a fixed HTTPS origin and redirects disabled.
// No writes are made to Loyverse.
public sealed class LoyverseClient(HttpClient http, LoyverseOptions options)
{
    public async Task<JsonElement> GetAsync(string path, CancellationToken ct, bool allowNotFound = false)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new LoyverseException("Save Loyverse__AccessToken in the backend environment first.");
        if (path.StartsWith('/') || path.Contains("://")) throw new ArgumentException("Invalid API path.");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.loyverse.com/v1.0/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            using var response = await http.SendAsync(request, ct);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return JsonSerializer.SerializeToElement(new { name = (string?)null });
            if (response.IsSuccessStatusCode)
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return json.RootElement.Clone();
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 << attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), ct);
                continue;
            }
            // Never return/log upstream response bodies: these may contain customer data.
            throw new LoyverseException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Loyverse rejected the access token. Check its expiry and value in Render.",
                HttpStatusCode.PaymentRequired => "Loyverse requires an active API/integration subscription for this account.",
                HttpStatusCode.Forbidden => "The Loyverse token does not have permission to read this resource.",
                HttpStatusCode.TooManyRequests => "Loyverse rate limit reached. Sync will retry later.",
                _ => $"Loyverse returned HTTP {(int)response.StatusCode}. No sync position was advanced."
            });
        }
        throw new LoyverseException("Loyverse is temporarily unavailable.");
    }

    public async Task<List<JsonElement>> GetPagesAsync(string path, string collection, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        var seen = new HashSet<string>();
        string? cursor = null;
        for (var page = 0; page < 100; page++)
        {
            var url = path + (path.Contains('?') ? "&" : "?") + "limit=250";
            if (cursor is not null) url += "&cursor=" + Uri.EscapeDataString(cursor);
            var json = await GetAsync(url, ct);
            if (!json.TryGetProperty(collection, out var items) || items.ValueKind != JsonValueKind.Array)
                throw new LoyverseException("Loyverse returned an unexpected response. Sync was not committed.");
            rows.AddRange(items.EnumerateArray().Select(x => x.Clone()));
            cursor = json.TryGetProperty("cursor", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(cursor)) return rows;
            if (!seen.Add(cursor)) throw new LoyverseException("Loyverse repeated a page cursor. Sync was not committed.");
            await Task.Delay(150, ct);
        }
        throw new LoyverseException("Sync exceeded 25,000 records. Narrow the initial date range before retrying.");
    }
}
