using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RaLaba.API.Integrations;

namespace RaLaba.API.Controllers;

public record RecordPayment(Guid RequestId, decimal Amount, DateOnly Date, string Method, string Note = "");

[ApiController, Authorize, Route("api/loyverse")]
public sealed class LoyverseController(NpgsqlDataSource database, LoyverseSync sync, LoyverseOptions options, LoyverseWorker worker) : ControllerBase
{
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        try { return Ok(await sync.RunAsync(ct)); }
        catch (LoyverseException ex) { return StatusCode(502, new { error = ex.Message }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return StatusCode(504, new { error = "Sync timed out safely. Retry; existing imports will not be duplicated." }); }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        await using var command = database.CreateCommand("select checkpoint,last_success,(select count(*) from ralaba_private.loyverse_receipts) from ralaba_private.loyverse_state where id=1");
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Ok(new { configured = !string.IsNullOrWhiteSpace(options.AccessToken), automatic = options.Enabled,
            checkpoint = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
            lastSuccess = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1), count = reader.GetInt64(2), error = worker.LastError });
    }

    [HttpGet("receipts")]
    public async Task<IActionResult> Receipts([FromQuery] int offset = 0, [FromQuery] bool unpaid = false, [FromQuery] string? number = null, CancellationToken ct = default)
    {
        if (offset < 0 || number?.Length > 200) return BadRequest(new { error = "Invalid page or receipt number." });
        await using var command = database.CreateCommand("""
            select r.receipt_number,r.receipt_date,r.customer_name,r.first_payload::text,r.payload::text,
              r.initial_received,r.initial_status,r.review_reason,r.source_changed,coalesce(p.received,0),
              coalesce(p.history,'[]'::jsonb)::text,count(*) over()
            from ralaba_private.loyverse_receipts r
            left join lateral (
              select sum(amount) received, jsonb_agg(jsonb_build_object('id',id,'amount',amount,'date',collected_date,'method',method,'note',note,'initial',origin_key like 'initial:%','revision',revision,
                'corrections',coalesce((select jsonb_agg(jsonb_build_object('before',before_value,'after',after_value,'reason',reason,'by',corrected_by,'at',corrected_at) order by corrected_at,request_id) from ralaba_private.payment_corrections c where c.payment_id=pay.id),'[]'::jsonb)) order by collected_date,recorded_at) history
              from ralaba_private.receipt_payments pay where merchant_id=r.merchant_id and receipt_number=r.receipt_number
            ) p on true
            where (@number='' or r.receipt_number=@number)
              and (not @unpaid or r.initial_received is null or r.source_changed or coalesce(p.received,0)<(r.first_payload->>'total_money')::numeric)
            order by r.receipt_date desc,r.receipt_number limit 100 offset @offset
            """);
        command.Parameters.AddWithValue("offset", offset); command.Parameters.AddWithValue("unpaid", unpaid);
        command.Parameters.AddWithValue("number", number ?? "");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<object>(); long count = 0;
        while (await reader.ReadAsync(ct))
        {
            using var first = JsonDocument.Parse(reader.GetString(3));
            using var latest = JsonDocument.Parse(reader.GetString(4));
            using var history = JsonDocument.Parse(reader.GetString(10));
            var total = PaymentRules.Total(first.RootElement); var received = reader.GetDecimal(9);
            var review = reader.IsDBNull(5) || reader.GetBoolean(8);
            count = reader.GetInt64(11);
            rows.Add(new { receiptNumber = reader.GetString(0), date = reader.GetDateTime(1), customer = reader.IsDBNull(2) ? null : reader.GetString(2),
                total, received, balance = review ? (decimal?)null : total - received,
                status = review ? "NEEDS_REVIEW" : PaymentRules.Status(total, received), initialStatus = reader.GetString(6),
                initialReceived = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                reviewReason = reader.GetBoolean(8) ? "Loyverse changed this receipt after import. Existing payments are preserved; reconcile before further payments." : reader.IsDBNull(7) ? null : reader.GetString(7),
                canReview = reader.IsDBNull(5) && !reader.GetBoolean(8) && PaymentRules.Text(latest.RootElement,"receipt_type") == "SALE" && PaymentRules.Text(latest.RootElement,"cancelled_at") is null,
                canCorrect = !reader.IsDBNull(5) && !reader.GetBoolean(8) && PaymentRules.Text(latest.RootElement,"receipt_type") == "SALE" && PaymentRules.Text(latest.RootElement,"cancelled_at") is null,
                note = PaymentRules.Text(first.RootElement,"note"),
                services = first.RootElement.TryGetProperty("line_items", out var items) ? items.EnumerateArray().Select(x => new { name = PaymentRules.Text(x,"item_name"), quantity = x.TryGetProperty("quantity",out var q) ? q.GetDecimal() : 0 }).ToArray() : [],
                payments = history.RootElement.Clone() });
        }
        return Ok(new { rows, count, offset, pageSize = 100 });
    }

    [HttpGet("collections")]
    public async Task<IActionResult> Collections([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 366) return BadRequest(new { error = "Choose a date range of up to one year." });
        await using var command = database.CreateCommand("""
            select collected_date,method,sum(amount),count(*)
            from ralaba_private.receipt_payments p
            join ralaba_private.loyverse_receipts r using(merchant_id,receipt_number)
            where collected_date between @from and @to and p.amount>0
            group by collected_date,method order by collected_date desc,method
            """);
        command.Parameters.AddWithValue("from", from); command.Parameters.AddWithValue("to", to);
        var days = new List<object>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) days.Add(new { date = reader.GetFieldValue<DateOnly>(0), method = reader.GetString(1), amount = reader.GetDecimal(2), entries = reader.GetInt64(3) });
        await using var pending = database.CreateCommand("select count(*) from ralaba_private.loyverse_receipts where initial_received is null or source_changed");
        return Ok(new { days, needsReview = (long)(await pending.ExecuteScalarAsync(ct))!, scope = "Recorded initial and later collections, including payments linked to receipts needing review. Source edits never remove recorded money. Refund payouts and legacy/manual receipts are not included." });
    }

    [HttpPost("receipts/{number}/payments")]
    public Task<IActionResult> AddPayment(string number, RecordPayment input, CancellationToken ct) => SavePayment(number,input,false,ct);

    [HttpPost("receipts/{number}/initial-payment")]
    public Task<IActionResult> ReviewInitial(string number, RecordPayment input, CancellationToken ct) => SavePayment(number,input,true,ct);

    private async Task<IActionResult> SavePayment(string number, RecordPayment input, bool initial, CancellationToken ct)
    {
        var method = (input.Method ?? "").Trim().ToUpperInvariant();
        var note = (input.Note ?? "").Trim();
        if (input.RequestId == Guid.Empty || !PaymentRules.ValidMoney(input.Amount) || (!initial && input.Amount == 0) || method is not ("CASH" or "GCASH") || note.Length > 500)
            return BadRequest(new { error = "Enter a valid amount (up to two decimals), Cash/GCash, and a request ID." });
        if (input.Date > PaymentRules.ManilaDate(DateTimeOffset.UtcNow) || input.Date == default)
            return BadRequest(new { error = "Payment date cannot be blank or in the future." });
        if (initial && string.IsNullOrWhiteSpace(note)) return BadRequest(new { error = "Explain the initial payment using your logbook or Excel record." });
        await using var db = await database.OpenConnectionAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        string merchant; JsonElement receipt; decimal? original;
        await using (var cmd = new NpgsqlCommand("select merchant_id,first_payload::text,initial_received,source_changed,initial_review_id,initial_review_note from ralaba_private.loyverse_receipts where receipt_number=@number for update",db,tx))
        {
            cmd.Parameters.AddWithValue("number", number);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return NotFound();
            merchant = reader.GetString(0);
            using var json = JsonDocument.Parse(reader.GetString(1)); receipt = json.RootElement.Clone();
            original = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
            if (initial && input.Amount==0 && !reader.IsDBNull(4) && reader.GetGuid(4)==input.RequestId && original==0 && reader.GetString(5)==note && input.Date==PaymentRules.ManilaDate(PaymentRules.Date(receipt,"receipt_date")))
                return Ok(new { saved=true, duplicate=true });
            if (reader.GetBoolean(3)) return Conflict(new { error = "The source receipt changed. Reconcile it before recording payments." });
        }
        // Same request replay is harmless; a reused ID with different data is rejected.
        await using (var replay = new NpgsqlCommand("select receipt_number,amount,collected_date,method,note,origin_key from ralaba_private.receipt_payments where id=@id",db,tx))
        {
            replay.Parameters.AddWithValue("id", input.RequestId);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return reader.GetString(0)==number && reader.GetDecimal(1)==input.Amount && reader.GetFieldValue<DateOnly>(2)==input.Date && reader.GetString(3)==method && reader.GetString(4)==note && reader.GetString(5).StartsWith(initial ? "initial:" : "local:")
                    ? Ok(new { saved=true, duplicate=true }) : Conflict(new { error="This request ID was already used for a different payment." });
        }
        if (PaymentRules.Text(receipt,"receipt_type") != "SALE" || PaymentRules.Text(receipt,"cancelled_at") is not null)
            return BadRequest(new { error="Refunds and cancelled receipts need reconciliation; do not record a sale payment against them." });
        var receiptDate = PaymentRules.ManilaDate(PaymentRules.Date(receipt,"receipt_date"));
        if (input.Date < receiptDate || (initial && input.Date != receiptDate))
            return BadRequest(new { error="Initial payments use the receipt date. Later payments cannot predate the receipt." });
        if (initial ? original.HasValue : !original.HasValue)
            return Conflict(new { error=initial ? "Initial payment already confirmed. Use Record payment for later collections." : "Confirm the initial payment first." });
        await using var sum = new NpgsqlCommand("select coalesce(sum(amount),0) from ralaba_private.receipt_payments where merchant_id=@merchant and receipt_number=@number",db,tx);
        sum.Parameters.AddWithValue("merchant",merchant); sum.Parameters.AddWithValue("number",number);
        var received = (decimal)(await sum.ExecuteScalarAsync(ct))!;
        var total = PaymentRules.Total(receipt);
        if (input.Amount > total-received) return Conflict(new { error="Payment exceeds the outstanding balance. Refresh the receipt." });
        if (!initial)
        {
            await using var duplicate = new NpgsqlCommand("select exists(select 1 from ralaba_private.receipt_payments where merchant_id=@merchant and receipt_number=@number and amount=@amount and collected_date=@date and method=@method and note=@note and origin_key like 'local:%')",db,tx);
            duplicate.Parameters.AddWithValue("merchant",merchant); duplicate.Parameters.AddWithValue("number",number);
            duplicate.Parameters.AddWithValue("amount",input.Amount); duplicate.Parameters.AddWithValue("date",input.Date);
            duplicate.Parameters.AddWithValue("method",method); duplicate.Parameters.AddWithValue("note",note);
            if ((bool)(await duplicate.ExecuteScalarAsync(ct))!) return Conflict(new { error="An identical payment already exists. If this is a separate collection, enter a distinct reference note." });
        }
        if (input.Amount > 0)
            await LoyverseSync.InsertPayment(db,tx,input.RequestId,merchant,number,input.Amount,input.Date,method,
                initial ? "initial:review" : "local:"+input.RequestId,User.FindFirstValue(ClaimTypes.NameIdentifier)!,note,ct);
        if (initial)
        {
            await using var update = new NpgsqlCommand("update ralaba_private.loyverse_receipts set initial_received=@amount,initial_status=@status,review_reason=null,initial_review_id=@id,initial_review_note=@note,initial_review_by=@actor where merchant_id=@merchant and receipt_number=@number",db,tx);
            update.Parameters.AddWithValue("amount",input.Amount); update.Parameters.AddWithValue("status",PaymentRules.Status(total,input.Amount));
            update.Parameters.AddWithValue("merchant",merchant); update.Parameters.AddWithValue("number",number);
            update.Parameters.AddWithValue("id",input.RequestId); update.Parameters.AddWithValue("note",note); update.Parameters.AddWithValue("actor",User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Ok(new { saved=true });
    }
}
