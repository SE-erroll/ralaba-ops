using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace RaLaba.API.Integrations;

public sealed class LoyverseSync(NpgsqlDataSource database, LoyverseClient client, LoyverseOptions options)
{
    public async Task<object> RunAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        ct = deadline.Token;
        await using var db = await database.OpenConnectionAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("select pg_try_advisory_xact_lock(814209611)", db, tx))
            if (!(bool)(await gate.ExecuteScalarAsync(ct))!) return new { busy = true };
        string? account;
        DateTimeOffset? checkpoint;
        await using (var command = new NpgsqlCommand("select merchant_id,checkpoint from ralaba_private.loyverse_state where id=1 for update", db, tx))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new LoyverseException("Install the integration schema first.");
            account = reader.IsDBNull(0) ? null : reader.GetString(0);
            checkpoint = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }
        var merchant = await client.GetAsync("merchant/", ct);
        var merchantId = PaymentRules.Text(merchant, "id") ?? throw new LoyverseException("Missing Loyverse merchant identity.");
        if (account is not null && account != merchantId) throw new LoyverseException("This token belongs to a different Loyverse business. Sync stopped.");
        if (merchant.GetProperty("currency").GetProperty("code").GetString() != "PHP")
            throw new LoyverseException("This importer expects a PHP account. Sync stopped to prevent currency errors.");
        var end = DateTimeOffset.UtcNow.AddSeconds(-30);
        var start = checkpoint?.AddMinutes(-10) ?? options.StartAt.ToUniversalTime().AddSeconds(-1);
        if (start >= end) return new { busy = false, fetched = 0 };
        var field = checkpoint.HasValue ? "updated_at" : "created_at";
        string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        var rows = await client.GetPagesAsync($"receipts?{field}_min={Iso(start)}&{field}_max={Iso(end)}", "receipts", ct);
        var paymentTypes = await client.GetAsync("payment_types?show_deleted=true", ct);
        var customers = new Dictionary<string, string?>();
        var inserted = 0;
        foreach (var row in rows)
        {
            var number = PaymentRules.Text(row, "receipt_number") ?? throw new LoyverseException("A receipt has no number.");
            var date = PaymentRules.Date(row, "receipt_date");
            var updated = PaymentRules.Date(row, "updated_at");
            // Incremental updates can include receipts older than the chosen initial window.
            // Keep only the agreed import scope, while retaining updates for existing records.
            if (PaymentRules.Date(row, "created_at") < options.StartAt.ToUniversalTime()) continue;
            var customerId = PaymentRules.Text(row, "customer_id");
            string? customer = null;
            if (!string.IsNullOrWhiteSpace(customerId))
            {
                if (!customers.TryGetValue(customerId, out customer))
                {
                    var person = await client.GetAsync("customers/" + Uri.EscapeDataString(customerId), ct, allowNotFound: true);
                    customer = PaymentRules.Text(person, "name");
                    customers[customerId] = customer;
                    await Task.Delay(150, ct);
                }
            }
            var initial = PaymentRules.Parse(row);
            await using var add = new NpgsqlCommand("""
                insert into ralaba_private.loyverse_receipts
                (merchant_id,receipt_number,receipt_date,source_updated_at,customer_name,first_payload,payload,initial_received,initial_status,review_reason)
                values (@merchant,@number,@date,@updated,@customer,@json,@json,@received,@status,@reason)
                on conflict (merchant_id,receipt_number) do nothing
                """, db, tx);
            add.Parameters.AddWithValue("merchant", merchantId);
            add.Parameters.AddWithValue("number", number);
            add.Parameters.AddWithValue("date", date.ToUniversalTime());
            add.Parameters.AddWithValue("updated", updated.ToUniversalTime());
            add.Parameters.AddWithValue("customer", NpgsqlDbType.Text, (object?)customer ?? DBNull.Value);
            add.Parameters.AddWithValue("json", NpgsqlDbType.Jsonb, row.GetRawText());
            add.Parameters.AddWithValue("received", NpgsqlDbType.Numeric, (object?)initial.Received ?? DBNull.Value);
            add.Parameters.AddWithValue("status", initial.Status);
            add.Parameters.AddWithValue("reason", NpgsqlDbType.Text, (object?)initial.ReviewReason ?? DBNull.Value);
            if (await add.ExecuteNonQueryAsync(ct) == 1)
            {
                inserted++;
                var index = 0;
                foreach (var payment in initial.Payments.Where(p => p.Amount > 0))
                    await InsertPayment(db, tx, Guid.NewGuid(), merchantId, number, payment.Amount, PaymentRules.ManilaDate(date), payment.Method,
                        "initial:" + index++, "loyverse-import", "Initial payment from receipt comment", ct);
            }
            else
            {
                // Preserve the initial decision and every local payment. Source edits are reviewable,
                // never silent rewrites of the collection history.
                await using var update = new NpgsqlCommand("""
                    update ralaba_private.loyverse_receipts set
                    source_changed = source_changed or
                      (first_payload->'total_money' is distinct from @json::jsonb->'total_money') or
                      (first_payload->'note' is distinct from @json::jsonb->'note') or
                      (first_payload->'receipt_type' is distinct from @json::jsonb->'receipt_type') or
                      (first_payload->'cancelled_at' is distinct from @json::jsonb->'cancelled_at') or
                      (first_payload->'receipt_date' is distinct from @json::jsonb->'receipt_date') or
                      (first_payload->'payments' is distinct from @json::jsonb->'payments'),
                    payload=@json, source_updated_at=@updated, customer_name=coalesce(@customer,customer_name)
                    where merchant_id=@merchant and receipt_number=@number and source_updated_at<=@updated
                    """, db, tx);
                foreach (var name in new[] { "merchant", "number", "json", "updated", "customer" })
                {
                    var p = add.Parameters[name];
                    update.Parameters.AddWithValue(name, p.NpgsqlDbType, p.Value!);
                }
                await update.ExecuteNonQueryAsync(ct);
            }
        }
        await using var save = new NpgsqlCommand("""
            update ralaba_private.loyverse_state set merchant_id=@merchant,checkpoint=@end,last_success=now(),payment_types=@types where id=1
            """, db, tx);
        save.Parameters.AddWithValue("merchant", merchantId);
        save.Parameters.AddWithValue("end", end.ToUniversalTime());
        save.Parameters.AddWithValue("types", NpgsqlDbType.Jsonb, paymentTypes.GetProperty("payment_types").GetRawText());
        await save.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new { busy = false, fetched = rows.Count, inserted, through = end };
    }

    internal static async Task InsertPayment(NpgsqlConnection db, NpgsqlTransaction tx, Guid id, string merchant, string number,
        decimal amount, DateOnly date, string method, string origin, string actor, string note, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            insert into ralaba_private.receipt_payments(id,merchant_id,receipt_number,amount,collected_date,method,origin_key,recorded_by,note)
            values (@id,@merchant,@number,@amount,@date,@method,@origin,@actor,@note)
            """, db, tx);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("merchant", merchant); cmd.Parameters.AddWithValue("number", number);
        cmd.Parameters.AddWithValue("amount", amount); cmd.Parameters.AddWithValue("date", date); cmd.Parameters.AddWithValue("method", method);
        cmd.Parameters.AddWithValue("origin", origin); cmd.Parameters.AddWithValue("actor", actor); cmd.Parameters.AddWithValue("note", note);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class LoyverseWorker(IServiceScopeFactory scopes, LoyverseOptions options, ILogger<LoyverseWorker> logger) : BackgroundService
{
    public string? LastError { get; private set; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<LoyverseSync>().RunAsync(stoppingToken);
                LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LastError = ex is LoyverseException ? ex.Message : "Sync could not finish. Retry or contact the owner; the previous sync position is preserved.";
                logger.LogWarning("Loyverse sync did not complete ({ErrorType}).", ex.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.IntervalSeconds, 60, 3600)), stoppingToken);
        }
    }
}
