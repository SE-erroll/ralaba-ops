using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RaLaba.API.Security;
using RaLaba.Application.DTOs;
using RaLaba.Application.Interfaces;
using Xunit;

namespace RaLaba.UnitTests;

public class ReceiptAccessTests
{
    private const string StaffId = "11111111-1111-4111-8111-111111111111";
    private sealed class AuthResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://test.supabase.co/auth/v1/user", request.RequestUri!.ToString());
            var token = request.Headers.Authorization?.Parameter;
            if (token == "unavailable") throw new HttpRequestException("Simulated Auth outage");
            var content = token switch {
                "staff" => $"{{\"id\":\"{StaffId}\",\"is_anonymous\":false}}",
                "outsider" => "{\"id\":\"22222222-2222-4222-8222-222222222222\"}",
                "anonymous" => $"{{\"id\":\"{StaffId}\",\"is_anonymous\":true}}",
                "broken" => "not-json",
                _ => null
            };
            return Task.FromResult(new HttpResponseMessage(content is null ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) {
                Content = new StringContent(content ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestApp : WebApplicationFactory<Program>
    {
        public Mock<IReceiptService> Receipts { get; } = new(MockBehavior.Strict);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=unused;Username=unused");
            builder.UseSetting("SupabaseAuth:Url", "https://test.supabase.co");
            builder.UseSetting("SupabaseAuth:PublishableKey", "sb_publishable_test");
            builder.UseSetting("SupabaseAuth:StaffUserIds:0", StaffId);
            builder.ConfigureServices(services => {
                services.RemoveAll<IReceiptService>();
                services.AddSingleton(Receipts.Object);
                services.AddHttpClient(SupabaseAuthenticationHandler.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new AuthResponse());
            });
        }
    }

    [Theory]
    [InlineData("GET", "/api/receipts")]
    [InlineData("GET", "/api/dashboard?from=2026-11-04&to=2026-11-07")]
    [InlineData("GET", "/api/loyverse/receipts")]
    [InlineData("GET", "/api/loyverse/collections")]
    [InlineData("GET", "/api/loyverse/status")]
    [InlineData("POST", "/api/loyverse/sync")]
    [InlineData("POST", "/api/loyverse/receipts/1-1/payments")]
    [InlineData("POST", "/api/loyverse/receipts/1-1/initial-payment")]
    [InlineData("GET", "/api/receipts/11111111-1111-4111-8111-111111111111")]
    [InlineData("POST", "/api/receipts")]
    [InlineData("DELETE", "/api/receipts/11111111-1111-4111-8111-111111111111")]
    public async Task UnauthenticatedRequests_AreDeniedBeforeServiceAccess(string method, string path)
    {
        using var app = new TestApp();
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Receipts.Invocations);
    }

    [Theory]
    [InlineData("forged", HttpStatusCode.Unauthorized)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("broken", HttpStatusCode.Unauthorized)]
    [InlineData("unavailable", HttpStatusCode.Unauthorized)]
    [InlineData("outsider", HttpStatusCode.Forbidden)]
    public async Task InvalidOrUnapprovedSessions_AreDenied(string token, HttpStatusCode expected)
    {
        using var app = new TestApp();
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/receipts");
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(app.Receipts.Invocations);
    }

    [Fact]
    public async Task ApprovedStaff_CanReadExistingReceipts()
    {
        using var app = new TestApp();
        app.Receipts.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ReceiptDto>());
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "staff");
        using var response = await client.GetAsync("/api/receipts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        app.Receipts.Verify(s => s.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
