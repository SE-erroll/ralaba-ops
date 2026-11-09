using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RaLaba.API.Integrations;

namespace RaLaba.API.Controllers;

[ApiController, Authorize, Route("api/dashboard")]
public sealed class DashboardController(NpgsqlDataSource database, LoyverseOptions options, LoyverseWorker worker) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 366)
            return BadRequest(new { error = "Choose a date range of up to one year." });

        // One statement gives every card the same database snapshot. Aggregate in the
        // database, not over the receipt screen's first page. Balances are CURRENT,
        // whereas sales and collections use their own dates in the selected period.
        await using var command = database.CreateCommand("""
            with paid as (
              select merchant_id,receipt_number,sum(amount) received
              from ralaba_private.receipt_payments group by merchant_id,receipt_number
            ), receipts as (
              select r.*, (r.receipt_date at time zone 'Asia/Manila')::date report_day,
                (r.first_payload->>'total_money')::numeric total,
                nullif(trim(r.first_payload->>'customer_id'),'') customer_id,
                coalesce(p.received,0) received,
                coalesce(r.payload->>'receipt_type'='SALE' and r.payload->>'cancelled_at' is null,false) sale,
                (r.initial_received is null or r.source_changed) review
              from ralaba_private.loyverse_receipts r
              left join paid p using(merchant_id,receipt_number)
            ), outstanding as (
              select *, greatest(total-received,0) balance from receipts
              where sale and not review
            ), daily_sales as (
              select report_day,sum(total) sales from receipts
              where report_day between @from and @to and sale and not source_changed group by report_day
            ), daily_collections as (
              select p.collected_date report_day,
                coalesce(sum(p.amount) filter(where p.method='CASH'),0) cash,
                coalesce(sum(p.amount) filter(where p.method='GCASH'),0) gcash,
                coalesce(sum(p.amount) filter(where r.review or not r.sale),0) flagged
              from ralaba_private.receipt_payments p join receipts r using(merchant_id,receipt_number)
              where p.collected_date between @from and @to group by p.collected_date
            ), daily as (
              select coalesce(s.report_day,c.report_day) report_day,coalesce(s.sales,0) sales,
                coalesce(c.cash,0) cash,coalesce(c.gcash,0) gcash,coalesce(c.flagged,0) flagged
              from daily_sales s full join daily_collections c using(report_day)
            )
            select jsonb_build_object(
              'sales',coalesce((select sum(sales) from daily),0),
              'cash',coalesce((select sum(cash) from daily),0),
              'gcash',coalesce((select sum(gcash) from daily),0),
              'flaggedCollections',coalesce((select sum(flagged) from daily),0),
              'reviewReceipts',(select count(*) from receipts where report_day between @from and @to and review and sale),
              'changedSalesAmount',coalesce((select sum(total) from receipts where report_day between @from and @to and source_changed and sale),0),
              'excludedReceipts',(select count(*) from receipts where report_day between @from and @to and not sale),
              'periodReceipts',(select count(*) from receipts where report_day between @from and @to),
              'unpaidPreview',coalesce((select jsonb_agg(jsonb_build_object('receiptNumber',receipt_number,'customer',customer_name,'date',report_day,'balance',balance,'partial',received>0) order by receipt_date,receipt_number) from (select * from outstanding where balance>0 order by receipt_date,receipt_number limit 3) o),'[]'::jsonb),
              'reviewPreview',coalesce((select jsonb_agg(jsonb_build_object('receiptNumber',receipt_number,'customer',customer_name,'date',report_day,'note',first_payload->>'note') order by receipt_date,receipt_number) from (select * from receipts where review and sale and report_day between @from and @to order by receipt_date,receipt_number limit 3) r),'[]'::jsonb),
              'current',jsonb_build_object(
                'outstanding',coalesce((select sum(balance) from outstanding),0),
                'unpaidReceipts',(select count(*) from outstanding where balance>0),
                'unpaidCustomers',(select count(distinct (merchant_id,customer_id)) from outstanding where balance>0 and customer_id is not null),
                'unlinkedUnpaidReceipts',(select count(*) from outstanding where balance>0 and customer_id is null),
                'needsReview',(select count(*) from receipts where review and sale)),
              'days',coalesce((select jsonb_agg(jsonb_build_object('date',report_day,'sales',sales,'cash',cash,'gcash',gcash,'flaggedCollections',flagged) order by report_day desc) from daily),'[]'::jsonb),
              'lastSuccess',(select last_success from ralaba_private.loyverse_state where id=1),
              'importedReceipts',(select count(*) from receipts)
            )::text
            """);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        using var json = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
        return Ok(new {
            from, to, generatedAt = DateTimeOffset.UtcNow, figures = json.RootElement.Clone(),
            sync = new { configured = !string.IsNullOrWhiteSpace(options.AccessToken), automatic = options.Enabled,
                intervalSeconds = Math.Clamp(options.IntervalSeconds,60,3600), error = worker.LastError }
        });
    }
}
