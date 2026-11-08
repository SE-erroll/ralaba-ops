using System.Net;
using System.Text.Json;
using RaLaba.API.Integrations;
using Xunit;

namespace RaLaba.UnitTests;

public class LoyverseTests
{
    static JsonElement Receipt(string? note, decimal amount=265, string type="SALE", string? cancelled=null, string method="Cash", decimal payment=265) => JsonSerializer.SerializeToElement(new {
        note, total_money=amount, receipt_type=type, cancelled_at=cancelled,
        receipt_date="2026-11-03T16:30:00Z", payments=new[]{new { name=method, money_amount=payment }} });

    [Fact] public void Unpaid_OverridesLoyverseTender()
    { var p=PaymentRules.Parse(Receipt("UNPAID")); Assert.Equal("UNPAID",p.Status); Assert.Equal(0,p.Received); Assert.Empty(p.Payments); }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("NOT PAID")] [InlineData("UNPAID PAID")] [InlineData("PARTIAL")]
    [InlineData("PARTIAL 265 CASH")] [InlineData("PARTIAL -1 CASH")] [InlineData("PARTIAL 10.001 CASH")]
    public void AmbiguousComments_NeverDefaultToPaid(string? note)
    { var p=PaymentRules.Parse(Receipt(note)); Assert.Equal("NEEDS_REVIEW",p.Status); Assert.Null(p.Received); Assert.Empty(p.Payments); }
    [Fact] public void Partial_UsesOnlyActualInitialAmount()
    { var p=PaymentRules.Parse(Receipt(" partial 100 gcash ")); Assert.Equal(100,p.Received); Assert.Equal("PARTIAL",p.Status); Assert.Equal("GCASH",Assert.Single(p.Payments).Method); }
    [Fact] public void Paid_RequiresMatchingTender()
    { Assert.Equal("PAID",PaymentRules.Parse(Receipt("PAID")).Status); Assert.Equal("NEEDS_REVIEW",PaymentRules.Parse(Receipt("PAID",payment:200)).Status); }
    [Fact] public void RefundsAndCancelledReceipts_AreNotCollections()
    { Assert.Empty(PaymentRules.Parse(Receipt("PAID",type:"REFUND")).Payments); Assert.Empty(PaymentRules.Parse(Receipt("PAID",cancelled:"2026-11-05T00:00:00Z")).Payments); }
    [Fact] public void ManilaBoundary_IsCorrect()
    { Assert.Equal(new DateOnly(2026,11,4),PaymentRules.ManilaDate(DateTimeOffset.Parse("2026-11-03T16:00:00Z"))); Assert.Equal(new DateOnly(2026,11,3),PaymentRules.ManilaDate(DateTimeOffset.Parse("2026-11-03T15:59:59Z"))); }
    [Fact] public void LaterPayments_ChangeBalanceWithoutChangingInitialStatus()
    { var initial=PaymentRules.Parse(Receipt("UNPAID")); Assert.Equal("PARTIAL",PaymentRules.Status(265,100)); Assert.Equal("PAID",PaymentRules.Status(265,265)); Assert.Equal("UNPAID",initial.Status); }
    [Fact] public void SourceEdits_AreDetected()
    { Assert.False(PaymentRules.FinancialChange(Receipt("UNPAID"),Receipt("UNPAID"))); Assert.True(PaymentRules.FinancialChange(Receipt("UNPAID"),Receipt("PAID"))); }

    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) => Task.FromResult(respond(request)); }
    static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content=new StringContent(value) };
    [Fact] public async Task Client_FollowsCursor_AndUsesReadOnlyFixedOrigin()
    {
        var calls=0;
        using var http=new HttpClient(new Handler(req=>{
            Assert.Equal(HttpMethod.Get,req.Method); Assert.Equal("api.loyverse.com",req.RequestUri!.Host);
            Assert.Equal("Bearer",req.Headers.Authorization!.Scheme); Assert.Equal("test-token",req.Headers.Authorization.Parameter);
            calls++; if(calls==1) return Json("{\"receipts\":[{\"receipt_number\":\"1\"}],\"cursor\":\"next\"}");
            Assert.Contains("cursor=next",req.RequestUri.Query); return Json("{\"receipts\":[{\"receipt_number\":\"2\"}]}");
        }));
        var client=new LoyverseClient(http,new(){AccessToken="test-token"});
        Assert.Equal(2,(await client.GetPagesAsync("receipts","receipts",default)).Count);
    }
    [Fact] public async Task RepeatedCursor_FailsInsteadOfAdvancingCheckpoint()
    {
        using var http=new HttpClient(new Handler(_=>Json("{\"receipts\":[],\"cursor\":\"same\"}")));
        await Assert.ThrowsAsync<LoyverseException>(()=>new LoyverseClient(http,new(){AccessToken="test"}).GetPagesAsync("receipts","receipts",default));
    }
    [Fact] public async Task Unauthorized_DoesNotExposeResponseOrToken()
    {
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.Unauthorized){Content=new StringContent("private-data")}));
        var ex=await Assert.ThrowsAsync<LoyverseException>(()=>new LoyverseClient(http,new(){AccessToken="secret-token"}).GetAsync("receipts",default));
        Assert.DoesNotContain("private-data",ex.Message); Assert.DoesNotContain("secret-token",ex.Message);
    }
}
