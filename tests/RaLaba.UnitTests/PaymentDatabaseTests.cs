using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RaLaba.API.Controllers;
using RaLaba.API.Integrations;
using RaLaba.API.Security;
using Xunit;

namespace RaLaba.UnitTests;

public sealed class LocalPostgresFactAttribute : FactAttribute
{
    public LocalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RALABA_TEST_POSTGRES")))
            Skip = "Set RALABA_TEST_POSTGRES to a disposable local PostgreSQL instance. No production connections allowed.";
    }
}

// Every test creates and drops its own randomly named local database. All external
// HTTP calls are fakes; the real controllers, importer and SQL run unchanged.
public sealed class PaymentDatabaseTests : IAsyncLifetime
{
    const string Staff = "11111111-1111-4111-8111-111111111111";
    readonly string name = "ralaba_test_" + Guid.NewGuid().ToString("N");
    NpgsqlConnectionStringBuilder? admin;
    TestApp? app;
    HttpClient client = null!;
    readonly FakeLoyverse source = new();

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("RALABA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        admin = new(configured);
        if (admin.Host is not ("127.0.0.1" or "localhost" or "::1") || admin.Database != "postgres")
            throw new InvalidOperationException("Tests require the postgres admin database on a disposable loopback server.");
        admin.Pooling = false;
        await Execute(admin.ConnectionString, $"create database {name}");
        var test = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
        await Execute(test.ConnectionString, """
            do $$ begin
              if not exists(select 1 from pg_roles where rolname='anon') then create role anon; end if;
              if not exists(select 1 from pg_roles where rolname='authenticated') then create role authenticated; end if;
            end $$;
            create table public.receipts(id integer primary key);
            """);
        await Execute(test.ConnectionString, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"loyverse-schema.sql")));
        app = new TestApp(test.ConnectionString, source);
        client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-staff");
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (app is not null) await app.DisposeAsync();
        if (admin is not null) await Execute(admin.ConnectionString, $"drop database if exists {name} with (force)");
    }
    static async Task Execute(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql,db);
        await command.ExecuteNonQueryAsync();
    }
    static JsonElement Receipt(string number="test-1", string? note="UNPAID", decimal total=265,
        string? customer="customer-1", string? cancelled=null, string type="SALE", string date="2020-11-03T16:30:00Z") =>
        JsonSerializer.SerializeToElement(new {receipt_number=number,note,total_money=total,customer_id=customer,
            receipt_type=type,cancelled_at=cancelled,receipt_date=date,created_at=date,
            updated_at=DateTimeOffset.UtcNow.AddMinutes(-1),payments=new[]{new{name="Cash",money_amount=total}},line_items=Array.Empty<object>()});
    async Task Sync()
    {
        using var response=await client.PostAsync("/api/loyverse/sync",null);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
    }
    async Task<JsonElement> Get(string path) => (await client.GetFromJsonAsync<JsonElement>(path));
    async Task<JsonElement> Dashboard(string from="2020-11-04",string to="2020-11-07") =>
        (await Get($"/api/dashboard?from={from}&to={to}")).GetProperty("figures");
    Task<HttpResponseMessage> Pay(decimal amount, string date, string method="CASH", string number="test-1", Guid? id=null, string note="test evidence", bool initial=false) =>
        client.PostAsJsonAsync($"/api/loyverse/receipts/{number}/{(initial?"initial-payment":"payments")}",
            new RecordPayment(id??Guid.NewGuid(),amount,DateOnly.Parse(date),method,note));

    Task<HttpResponseMessage> Correct(Guid payment, decimal amount, string date="2020-11-05", string method="GCASH", int revision=0, Guid? request=null, string reason="Checked logbook", string number="test-1") =>
        client.PostAsJsonAsync($"/api/loyverse/receipts/{number}/payments/{payment}/corrections",
            new CorrectPayment(request??Guid.NewGuid(),revision,amount,DateOnly.Parse(date),method,reason));

    [LocalPostgresFact]
    public async Task Correction_MovesInitialToActualDay_PreservesAudit_AndResyncCannotRestoreOldPayment()
    {
        source.Rows=[Receipt(note:"PAID")]; await Sync();
        var payment=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0].GetProperty("payments")[0].GetProperty("id").GetGuid();
        var request=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Correct(payment,200,request:request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Correct(payment,200,request:request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Correct(payment,190,request:request)).StatusCode);
        await Sync();
        var row=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0];
        Assert.Equal(0,row.GetProperty("initialReceived").GetDecimal());
        Assert.Equal("UNPAID",row.GetProperty("initialStatus").GetString());
        Assert.Equal(65,row.GetProperty("balance").GetDecimal());
        var history=row.GetProperty("payments")[0];
        Assert.False(history.GetProperty("initial").GetBoolean());
        Assert.Equal(1,history.GetProperty("revision").GetInt32());
        var audit=history.GetProperty("corrections"); Assert.Equal(1,audit.GetArrayLength());
        Assert.Equal(265,audit[0].GetProperty("before").GetProperty("amount").GetDecimal());
        Assert.Equal(200,audit[0].GetProperty("after").GetProperty("amount").GetDecimal());
        Assert.Equal(Staff,audit[0].GetProperty("by").GetString());
        Assert.Equal(0,(await Dashboard("2020-11-04","2020-11-04")).GetProperty("cash").GetDecimal());
        Assert.Equal(200,(await Dashboard("2020-11-05","2020-11-05")).GetProperty("gcash").GetDecimal());
        Assert.Equal(265,(await Dashboard()).GetProperty("sales").GetDecimal());
        Assert.Equal(HttpStatusCode.OK,(await Pay(65,"2020-11-06")).StatusCode);
    }

    [LocalPostgresFact]
    public async Task Correction_VoidsMistake_KeepsHistory_AndAllowsReentry()
    {
        source.Rows=[Receipt(note:"PAID")]; await Sync();
        var payment=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0].GetProperty("payments")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await Correct(payment,0,"2020-11-04","CASH")).StatusCode);
        var row=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0];
        Assert.Equal(265,row.GetProperty("balance").GetDecimal());
        Assert.Equal(0,row.GetProperty("initialReceived").GetDecimal());
        Assert.Equal(1,row.GetProperty("payments").GetArrayLength());
        var collections=await Get("/api/loyverse/collections?from=2020-11-04&to=2020-11-07");
        Assert.Equal(0,collections.GetProperty("days").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK,(await Pay(265,"2020-11-05")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Correct(payment,1,"2020-11-04","CASH",revision:1)).StatusCode);
    }

    [LocalPostgresFact]
    public async Task Correction_RejectsInvalidData_AndConcurrentEditsCannotOverwrite()
    {
        source.Rows=[Receipt(),Receipt("other")]; await Sync();
        var id=Guid.NewGuid(); Assert.Equal(HttpStatusCode.OK,(await Pay(100,"2020-11-05",id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(id,10,reason:"")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(id,10,"2020-11-03")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(id,10,"2099-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(id,10,method:"BANK")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(id,1.001m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Correct(id,10,number:"other")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Correct(id,266)).StatusCode);
        var edits=await Task.WhenAll(Correct(id,150),Correct(id,200));
        Assert.Single(edits.Where(r=>r.StatusCode==HttpStatusCode.OK));
        Assert.Single(edits.Where(r=>r.StatusCode==HttpStatusCode.Conflict));
        var row=(await Get("/api/loyverse/receipts?number=test-1")).GetProperty("rows")[0];
        Assert.Equal(1,row.GetProperty("payments")[0].GetProperty("corrections").GetArrayLength());
        source.Rows=[Receipt(note:"changed")]; await Sync();
        Assert.Equal(HttpStatusCode.Conflict,(await Correct(id,0,revision:1)).StatusCode);
    }

    [LocalPostgresFact]
    public async Task UnpaidToPartialToPaid_UsesPaymentDates_AndRetriesAndResyncDoNotDuplicate()
    {
        source.Rows=[Receipt()]; await Sync(); await Sync();
        var first=await Dashboard(); Assert.Equal(265,first.GetProperty("sales").GetDecimal()); Assert.Equal(0,first.GetProperty("cash").GetDecimal());
        var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Pay(100,"2020-11-05",id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Pay(100,"2020-11-05",id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Pay(99,"2020-11-05",id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Pay(100,"2020-11-05")).StatusCode);
        var partial=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0];
        Assert.Equal("PARTIAL",partial.GetProperty("status").GetString());
        Assert.Equal("UNPAID",partial.GetProperty("initialStatus").GetString());
        Assert.Equal(165,partial.GetProperty("balance").GetDecimal());
        await Sync();
        Assert.Equal(HttpStatusCode.OK,(await Pay(165,"2020-11-07","GCASH")).StatusCode);
        await Sync();
        var final=(await Get("/api/loyverse/receipts")).GetProperty("rows");
        Assert.Equal(1,final.GetArrayLength()); Assert.Equal("PAID",final[0].GetProperty("status").GetString());
        Assert.Equal(2,final[0].GetProperty("payments").GetArrayLength());
        var sale=await Dashboard("2020-11-04","2020-11-04");
        Assert.Equal(265,sale.GetProperty("sales").GetDecimal()); Assert.Equal(0,sale.GetProperty("cash").GetDecimal());
        var cash=await Dashboard("2020-11-05","2020-11-05");
        Assert.Equal(0,cash.GetProperty("sales").GetDecimal()); Assert.Equal(100,cash.GetProperty("cash").GetDecimal());
        var gcash=await Dashboard("2020-11-07","2020-11-07"); Assert.Equal(165,gcash.GetProperty("gcash").GetDecimal());
        Assert.Equal(0,gcash.GetProperty("current").GetProperty("outstanding").GetDecimal());
    }

    [LocalPostgresFact]
    public async Task SourceEditAndCancellation_PreserveCollections_ButFlagThem()
    {
        source.Rows=[Receipt(note:"PAID")]; await Sync();
        source.Rows=[Receipt(note:"edited note")]; await Sync();
        var changed=await Dashboard();
        Assert.Equal(265,changed.GetProperty("cash").GetDecimal()); Assert.Equal(265,changed.GetProperty("flaggedCollections").GetDecimal());
        Assert.Equal(0,changed.GetProperty("sales").GetDecimal()); Assert.Equal(265,changed.GetProperty("changedSalesAmount").GetDecimal());
        var collections=await Get("/api/loyverse/collections?from=2020-11-04&to=2020-11-07");
        Assert.Equal(265,collections.GetProperty("days")[0].GetProperty("amount").GetDecimal());
        source.Rows=[Receipt(note:"edited note",cancelled:"2020-11-05T00:00:00Z")]; await Sync();
        var cancelled=await Dashboard(); Assert.Equal(265,cancelled.GetProperty("cash").GetDecimal());
        Assert.Equal(1,cancelled.GetProperty("excludedReceipts").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await Pay(1,"2020-11-07")).StatusCode);
    }

    [LocalPostgresFact]
    public async Task ConcurrentPayments_CannotOverpay_AndConcurrentRetriesSaveOnce()
    {
        source.Rows=[Receipt()]; await Sync();
        var attempts=await Task.WhenAll(Pay(200,"2020-11-05",note:"cash A"),Pay(200,"2020-11-05",note:"cash B"));
        Assert.Single(attempts.Where(r=>r.StatusCode==HttpStatusCode.OK));
        Assert.Single(attempts.Where(r=>r.StatusCode==HttpStatusCode.Conflict));
        var id=Guid.NewGuid();
        var retries=await Task.WhenAll(Pay(65,"2020-11-06",id:id),Pay(65,"2020-11-06",id:id));
        Assert.All(retries,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));
        var receipt=(await Get("/api/loyverse/receipts")).GetProperty("rows")[0];
        Assert.Equal(265,receipt.GetProperty("received").GetDecimal()); Assert.Equal(2,receipt.GetProperty("payments").GetArrayLength());
    }

    [LocalPostgresFact]
    public async Task UnknownInitialPayment_RequiresEvidence_ThenAllowsLaterPayment()
    {
        source.Rows=[Receipt(note:null)]; await Sync();
        Assert.Equal(HttpStatusCode.Conflict,(await Pay(100,"2020-11-05")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Pay(100,"2020-11-04",initial:true,note:"")).StatusCode);
        var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,(await Pay(0,"2020-11-04",initial:true,id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Pay(0,"2020-11-04",initial:true,id:id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Pay(100,"2020-11-03")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Pay(266,"2020-11-05")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Pay(100,"2020-11-05")).StatusCode);
        Assert.Equal(165,(await Dashboard()).GetProperty("current").GetProperty("outstanding").GetDecimal());
    }

    [LocalPostgresFact]
    public async Task Dashboard_CountsAllPages_DistinctCustomers_UnknownAndExcludedSeparately()
    {
        source.Rows=Enumerable.Range(0,105).Select(i=>Receipt(number:$"many-{i}",total:10,customer:i<104?"same-customer":null))
            .Concat([Receipt("unknown",null,20),Receipt("cancelled","PAID",30,cancelled:"2020-11-05T00:00:00Z"),Receipt("refund","PAID",40,type:"REFUND"),
                Receipt("before","UNPAID",50,date:"2020-11-03T15:59:59Z")]).ToArray();
        await Sync();
        var result=await Dashboard();
        Assert.Equal(1070,result.GetProperty("sales").GetDecimal());
        Assert.Equal(1,result.GetProperty("reviewReceipts").GetInt32()); Assert.Equal(2,result.GetProperty("excludedReceipts").GetInt32());
        var current=result.GetProperty("current"); Assert.Equal(1100,current.GetProperty("outstanding").GetDecimal());
        // 'before' has a different customer ID but the same display name.
        Assert.Equal(2,current.GetProperty("unpaidCustomers").GetInt32()); Assert.Equal(1,current.GetProperty("unlinkedUnpaidReceipts").GetInt32());
        Assert.Equal(106,current.GetProperty("unpaidReceipts").GetInt32());
        Assert.Equal(100,(await Get("/api/loyverse/receipts")).GetProperty("rows").GetArrayLength());
        var selected=(await Get("/api/loyverse/receipts?number=many-104")).GetProperty("rows");
        Assert.Equal(1,selected.GetArrayLength()); Assert.Equal("many-104",selected[0].GetProperty("receiptNumber").GetString());
    }

    [LocalPostgresFact]
    public async Task Dashboard_EmptyRangeAndInvalidDates_AreExplicit()
    {
        var empty=await Dashboard(); Assert.Equal(0,empty.GetProperty("sales").GetDecimal());
        Assert.Equal(0,empty.GetProperty("days").GetArrayLength()); Assert.Equal(JsonValueKind.Null,empty.GetProperty("lastSuccess").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/dashboard?from=2020-11-07&to=2020-11-04")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/dashboard")).StatusCode);
    }

    sealed class FakeLoyverse : HttpMessageHandler
    {
        public JsonElement[] Rows { get; set; }=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get,request.Method); Assert.Equal("api.loyverse.com",request.RequestUri!.Host);
            object body=request.RequestUri.AbsolutePath switch {
                "/v1.0/merchant/" => new { id="test-merchant", currency=new{code="PHP"} },
                "/v1.0/receipts" => new { receipts=Rows },
                "/v1.0/payment_types" => new { payment_types=Array.Empty<object>() },
                var path when path.StartsWith("/v1.0/customers/") => new { name="Sample customer" },
                _ => throw new InvalidOperationException("Unexpected test request")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body)});
        }
    }
    sealed class FakeAuth : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{id=Staff,is_anonymous=false})});
    }
    sealed class TestApp(string connection,FakeLoyverse source) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection",connection);
            builder.UseSetting("SupabaseAuth:Url","https://test.supabase.co"); builder.UseSetting("SupabaseAuth:PublishableKey","sb_publishable_test");
            builder.UseSetting("SupabaseAuth:StaffUserIds:0",Staff);
            builder.UseSetting("Loyverse:Enabled","false"); builder.UseSetting("Loyverse:AccessToken","test-token"); builder.UseSetting("Loyverse:StartAt","2020-11-01T00:00:00+08:00");
            builder.ConfigureServices(services=> {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddHttpClient<LoyverseClient>().ConfigurePrimaryHttpMessageHandler(()=>source);
                services.AddHttpClient(SupabaseAuthenticationHandler.HttpClientName).ConfigurePrimaryHttpMessageHandler(()=>new FakeAuth());
            });
        }
    }
}
