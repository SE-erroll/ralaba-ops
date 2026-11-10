using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using RaLaba.API.Integrations;

namespace RaLaba.API.Controllers;

public record CorrectPayment(Guid RequestId, int Revision, decimal Amount, DateOnly Date, string Method, string Reason);

[ApiController, Authorize, Route("api/loyverse/receipts/{number}/payments/{paymentId:guid}/corrections")]
public sealed class PaymentCorrectionsController(NpgsqlDataSource database) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Correct(string number, Guid paymentId, CorrectPayment input, CancellationToken ct)
    {
        var method = (input.Method ?? "").Trim().ToUpperInvariant();
        var reason = (input.Reason ?? "").Trim();
        if (input.RequestId == Guid.Empty || input.Revision < 0 || !PaymentRules.ValidMoney(input.Amount)
            || method is not ("CASH" or "GCASH") || reason.Length is < 1 or > 500)
            return BadRequest(new { error = "Enter a valid amount, Cash/GCash and a reason for the correction." });
        if (input.Date == default || input.Date > PaymentRules.ManilaDate(DateTimeOffset.UtcNow))
            return BadRequest(new { error = "Choose the actual collection date, no later than today." });

        await using var db = await database.OpenConnectionAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        string merchant; decimal total; DateOnly receiptDate; bool blocked;
        // Same lock order as initial/later payment entry and sync. Two staff cannot
        // change a balance concurrently or silently overwrite one another's edit.
        await using (var receipt = new NpgsqlCommand("select merchant_id,first_payload::text,source_changed,initial_received from ralaba_private.loyverse_receipts where receipt_number=@number for update", db, tx))
        {
            receipt.Parameters.AddWithValue("number", number);
            await using var reader = await receipt.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return NotFound();
            merchant = reader.GetString(0);
            using var json = JsonDocument.Parse(reader.GetString(1));
            total = PaymentRules.Total(json.RootElement);
            receiptDate = PaymentRules.ManilaDate(PaymentRules.Date(json.RootElement, "receipt_date"));
            blocked = reader.GetBoolean(2) || reader.IsDBNull(3) || PaymentRules.Text(json.RootElement, "receipt_type") != "SALE"
                || PaymentRules.Text(json.RootElement, "cancelled_at") is not null;
        }
        await using (var replay = new NpgsqlCommand("select payment_id,before_value,after_value,reason from ralaba_private.payment_corrections where request_id=@id", db, tx))
        {
            replay.Parameters.AddWithValue("id", input.RequestId);
            await using var reader = await replay.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                using var before = JsonDocument.Parse(reader.GetString(1));
                using var after = JsonDocument.Parse(reader.GetString(2));
                var value = after.RootElement;
                return reader.GetGuid(0) == paymentId && value.GetProperty("receipt_number").GetString() == number
                    && before.RootElement.GetProperty("revision").GetInt32() == input.Revision
                    && value.GetProperty("amount").GetDecimal() == input.Amount
                    && value.GetProperty("collected_date").GetString() == input.Date.ToString("yyyy-MM-dd")
                    && value.GetProperty("method").GetString() == method && reader.GetString(3) == reason
                    ? Ok(new { saved = true, duplicate = true })
                    : Conflict(new { error = "This correction request was already used with different details." });
            }
        }
        if (blocked) return Conflict(new { error = "This receipt needs reconciliation before its payments can be corrected." });
        if (input.Date < receiptDate) return BadRequest(new { error = "Collection date cannot be before the receipt date." });

        string beforeJson; string origin;
        await using (var payment = new NpgsqlCommand("select to_jsonb(p)::text from ralaba_private.receipt_payments p where id=@id and merchant_id=@merchant and receipt_number=@number for update", db, tx))
        {
            payment.Parameters.AddWithValue("id", paymentId); payment.Parameters.AddWithValue("merchant", merchant); payment.Parameters.AddWithValue("number", number);
            beforeJson = (string?)await payment.ExecuteScalarAsync(ct) ?? "";
            if (beforeJson.Length == 0) return NotFound();
        }
        using (var before = JsonDocument.Parse(beforeJson))
        {
            var value = before.RootElement;
            if (value.GetProperty("revision").GetInt32() != input.Revision)
                return Conflict(new { error = "Someone already corrected this payment. Refresh and check its history." });
            if (value.GetProperty("amount").GetDecimal() == input.Amount && value.GetProperty("method").GetString() == method
                && value.GetProperty("collected_date").GetString() == input.Date.ToString("yyyy-MM-dd"))
                return BadRequest(new { error = "Change the amount, date or method before saving a correction." });
            origin = value.GetProperty("origin_key").GetString()!;
        }
        await using (var sum = new NpgsqlCommand("select coalesce(sum(amount),0) from ralaba_private.receipt_payments where merchant_id=@merchant and receipt_number=@number and id<>@id", db, tx))
        {
            sum.Parameters.AddWithValue("merchant", merchant); sum.Parameters.AddWithValue("number", number); sum.Parameters.AddWithValue("id", paymentId);
            if (input.Amount + (decimal)(await sum.ExecuteScalarAsync(ct))! > total)
                return Conflict(new { error = "The corrected payments would exceed the receipt total." });
        }
        // Moving an initial payment to a later day must also remove that money
        // from the original day's initial decision. Resync never recreates it.
        var wasInitial = origin.StartsWith("initial:");
        if (wasInitial && input.Date != receiptDate) origin = "local:" + paymentId;
        string afterJson;
        await using (var update = new NpgsqlCommand("update ralaba_private.receipt_payments p set amount=@amount,collected_date=@date,method=@method,origin_key=@origin,revision=revision+1 where id=@id returning to_jsonb(p)::text", db, tx))
        {
            update.Parameters.AddWithValue("amount", input.Amount); update.Parameters.AddWithValue("date", input.Date);
            update.Parameters.AddWithValue("method", method); update.Parameters.AddWithValue("origin", origin); update.Parameters.AddWithValue("id", paymentId);
            afterJson = (string)(await update.ExecuteScalarAsync(ct))!;
        }
        if (wasInitial)
        {
            await using var update = new NpgsqlCommand("""
                update ralaba_private.loyverse_receipts r set initial_received=s.amount,
                  initial_status=case when s.amount>=@total then 'PAID' when s.amount>0 then 'PARTIAL' else 'UNPAID' end
                from (select coalesce(sum(amount),0) amount from ralaba_private.receipt_payments
                  where merchant_id=@merchant and receipt_number=@number and origin_key like 'initial:%') s
                where r.merchant_id=@merchant and r.receipt_number=@number
                """, db, tx);
            update.Parameters.AddWithValue("total", total); update.Parameters.AddWithValue("merchant", merchant); update.Parameters.AddWithValue("number", number);
            await update.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new NpgsqlCommand("insert into ralaba_private.payment_corrections(request_id,payment_id,before_value,after_value,reason,corrected_by) values(@request,@payment,@before,@after,@reason,@actor)", db, tx))
        {
            audit.Parameters.AddWithValue("request", input.RequestId); audit.Parameters.AddWithValue("payment", paymentId);
            audit.Parameters.AddWithValue("before", NpgsqlDbType.Jsonb, beforeJson); audit.Parameters.AddWithValue("after", NpgsqlDbType.Jsonb, afterJson);
            audit.Parameters.AddWithValue("reason", reason); audit.Parameters.AddWithValue("actor", User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Ok(new { saved = true });
    }
}
