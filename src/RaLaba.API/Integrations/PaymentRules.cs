using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RaLaba.API.Integrations;

public record InitialPayment(decimal Amount, string Method);
public record InitialDecision(string Status, decimal? Received, List<InitialPayment> Payments, string? ReviewReason);

public static class PaymentRules
{
    public static string? Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static DateTimeOffset Date(JsonElement value, string key) =>
        DateTimeOffset.Parse(Text(value, key) ?? throw new ArgumentException($"Missing {key}."), CultureInfo.InvariantCulture).ToUniversalTime();
    public static decimal Total(JsonElement receipt) => receipt.GetProperty("total_money").GetDecimal();
    public static DateOnly ManilaDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(8)).DateTime);
    public static string Status(decimal total, decimal received) => received >= total ? "PAID" : received > 0 ? "PARTIAL" : "UNPAID";
    public static bool ValidMoney(decimal value) => value >= 0 && decimal.Round(value, 2) == value;

    public static InitialDecision Parse(JsonElement receipt)
    {
        InitialDecision Review(string why) => new("NEEDS_REVIEW", null, [], why);
        if (Text(receipt, "cancelled_at") is not null) return Review("Cancelled receipt: excluded from collections pending reconciliation.");
        if (Text(receipt, "receipt_type") != "SALE") return Review("Refund: needs separate reconciliation; not counted as a new sale or collection.");
        var total = Total(receipt);
        if (!ValidMoney(total)) return Review("Invalid or unsupported receipt amount.");
        var note = (Text(receipt, "note") ?? "").Trim().ToUpperInvariant();
        if (note == "UNPAID") return new("UNPAID", 0, [], null);
        // Only explicit whole comments are accepted. Never match PAID inside UNPAID/NOT PAID.
        if (note == "PAID")
        {
            if (total == 0) return new("PAID", 0, [], null);
            if (!receipt.TryGetProperty("payments", out var payments) || payments.ValueKind != JsonValueKind.Array)
                return Review("PAID comment has no payment breakdown.");
            var parts = new List<InitialPayment>();
            foreach (var payment in payments.EnumerateArray())
            {
                var method = (Text(payment, "name") ?? "").Trim().ToUpperInvariant();
                if (method is not ("CASH" or "GCASH")) return Review("Confirm the actual payment method.");
                if (!payment.TryGetProperty("money_amount", out var amount) || !amount.TryGetDecimal(out var money) || !ValidMoney(money))
                    return Review("Invalid payment breakdown.");
                parts.Add(new(money, method));
            }
            if (parts.Sum(x => x.Amount) != total) return Review("Payment breakdown differs from receipt total.");
            return new("PAID", total, parts, null);
        }
        var partial = Regex.Match(note, @"^PARTIAL\s+(\d+(?:\.\d{1,2})?)\s+(CASH|GCASH)$", RegexOptions.CultureInvariant);
        if (partial.Success && decimal.TryParse(partial.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var received)
            && received > 0 && received < total)
            return new("PARTIAL", received, [new(received, partial.Groups[2].Value)], null);
        return Review("Use PAID, UNPAID, or PARTIAL 100 CASH/GCASH; otherwise confirm the initial payment below.");
    }

    public static bool FinancialChange(JsonElement original, JsonElement latest) =>
        Total(original) != Total(latest) || Text(original, "note") != Text(latest, "note") ||
        Text(original, "receipt_type") != Text(latest, "receipt_type") ||
        Text(original, "cancelled_at") != Text(latest, "cancelled_at") ||
        Text(original, "receipt_date") != Text(latest, "receipt_date") ||
        (original.TryGetProperty("payments", out var a) ? a.GetRawText() : "") !=
        (latest.TryGetProperty("payments", out var b) ? b.GetRawText() : "");
}
