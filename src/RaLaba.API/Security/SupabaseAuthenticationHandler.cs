using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RaLaba.API.Security;

// Verify tokens with the configured Auth server; decoded JWT claims alone are untrusted.
public sealed class SupabaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Supabase";
    public const string HttpClientName = "SupabaseAuth";
    private readonly IHttpClientFactory _clients;
    private readonly SupabaseAuthOptions _auth;

    public SupabaseAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, IHttpClientFactory clients,
        IOptions<SupabaseAuthOptions> auth) : base(options, logger, encoder)
    {
        _clients = clients;
        _auth = auth.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return AuthenticateResult.NoResult();
        if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter) || header.Parameter.Length > 16384)
            return AuthenticateResult.Fail("Invalid authorization header.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_auth.Url.TrimEnd('/')}/auth/v1/user");
        request.Headers.Authorization = header;
        request.Headers.Add("apikey", _auth.PublishableKey);
        try
        {
            using var response = await _clients.CreateClient(HttpClientName)
                .SendAsync(request, Context.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return AuthenticateResult.Fail("Session could not be verified.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
            var user = body.RootElement;
            if (user.ValueKind != JsonValueKind.Object
                || !user.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !Guid.TryParse(id.GetString(), out var userId) || userId == Guid.Empty
                || (user.TryGetProperty("is_anonymous", out var anonymous)
                    && anonymous.ValueKind != JsonValueKind.False))
                return AuthenticateResult.Fail("A staff account is required.");
            var identity = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            }, SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Logger.LogWarning("Staff session verification is unavailable.");
            return AuthenticateResult.Fail("Session could not be verified.");
        }
    }
}
