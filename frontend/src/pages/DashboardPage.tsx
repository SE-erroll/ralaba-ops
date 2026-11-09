import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { request } from "../lib/api";
import "./DashboardPage.css";

type Day = { date: string; sales: number; cash: number; gcash: number; flaggedCollections: number };
type Dashboard = {
  from: string; to: string; generatedAt: string;
  figures: {
    sales: number; cash: number; gcash: number; flaggedCollections: number;
    reviewReceipts: number; changedSalesAmount: number; excludedReceipts: number; periodReceipts: number;
    current: { outstanding: number; unpaidReceipts: number; unpaidCustomers: number; unlinkedUnpaidReceipts: number; needsReview: number };
    unpaidPreview: { receiptNumber: string; customer: string | null; date: string; balance: number; partial: boolean }[];
    reviewPreview: { receiptNumber: string; customer: string | null; date: string; note: string | null }[];
    days: Day[]; lastSuccess: string | null; importedReceipts: number;
  };
  sync: { configured: boolean; automatic: boolean; intervalSeconds: number; error: string | null };
};
const money = (value: number) => new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" }).format(value);
const today = () => new Date(Date.now() + 8 * 3600000).toISOString().slice(0, 10);
const time = (value: string) => new Date(value).toLocaleString("en-PH", { timeZone: "Asia/Manila" });

function Card({ label, value, detail, highlight = false }: { label: string; value: string | number; detail: string; highlight?: boolean }) {
  return <div className={`dashboard-card ${highlight ? "dashboard-card-dark" : ""}`}>
    <dt className="text-sm font-medium text-gray-600">{label}</dt>
    <dd className="mt-2 break-words text-2xl font-semibold tracking-tight">{value}</dd>
    <dd className="mt-2 text-sm text-gray-600">{detail}</dd>
  </div>;
}

export default function DashboardPage() {
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [range, setRange] = useState(() => ({ from: today(), to: today() }));
  const [refresh, setRefresh] = useState(0);
  const [data, setData] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [rangeError, setRangeError] = useState("");
  useEffect(() => {
    const abort = new AbortController();
    let running = false;
    async function load() {
      if (running) return;
      running = true;
      setLoading(true); setError(""); setData(null);
      try {
        const result = await request<Dashboard>(`/dashboard?from=${range.from}&to=${range.to}`, { signal: abort.signal });
        if (!abort.signal.aborted) setData(result);
      } catch (err) {
        if (!abort.signal.aborted) setError(err instanceof Error ? err.message : "Dashboard could not load. Please retry.");
      } finally {
        running = false;
        if (!abort.signal.aborted) setLoading(false);
      }
    }
    void load();
    const timer = window.setInterval(() => { void load(); }, 60000);
    return () => { abort.abort(); window.clearInterval(timer); };
  }, [range, refresh]);
  function apply(event: FormEvent) {
    event.preventDefault();
    if (!from || !to || to < from || (Date.parse(to) - Date.parse(from)) / 86400000 > 366) {
      setRangeError("Choose an end date on or after the start, within one year."); return;
    }
    setRangeError(""); setRange({ from, to });
  }
  const figures = data?.figures;
  const stale = data && (!data.figures.lastSuccess || Date.now() - Date.parse(data.figures.lastSuccess) > Math.max(900, data.sync.intervalSeconds * 3) * 1000);
  const chartDays = figures?.days.slice().reverse() ?? [];
  const chartMax = Math.max(1, ...chartDays.flatMap(day => [day.sales, day.cash + day.gcash]));
  return <main className="dashboard mx-auto max-w-6xl p-4 sm:p-6">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><p className="dashboard-eyebrow">Laundry management</p><h1 className="mt-2 text-3xl font-bold tracking-tight">Ra Laba, at a glance.</h1><p className="mt-2 text-sm text-gray-600">Sales, collections and unpaid customers · Philippine time</p></div>
      <Link className="rounded-lg border bg-white px-4 py-2 text-blue-800" to="/loyverse">Receipts & payments</Link>
    </div>
    <form onSubmit={apply} className="my-5 flex flex-wrap items-end gap-3">
      <label className="text-sm font-medium">From<input className="mt-1 block rounded-lg border bg-white p-2" type="date" required value={from} onChange={e => setFrom(e.target.value)} /></label>
      <label className="text-sm font-medium">To<input className="mt-1 block rounded-lg border bg-white p-2" type="date" required min={from} value={to} onChange={e => setTo(e.target.value)} /></label>
      <button className="dashboard-button">Show dates</button>
      <button type="button" className="rounded-lg border bg-white px-4 py-2 disabled:opacity-50" disabled={loading} onClick={() => setRefresh(value => value + 1)}>Refresh</button>
    </form>
    {rangeError && <p role="alert" className="mb-4 text-red-700">{rangeError}</p>}
    {loading && <p role="status" className="rounded-lg border bg-white p-6">Loading dashboard…</p>}
    {error && <p role="alert" className="rounded-lg border border-red-200 bg-red-50 p-4 text-red-800">Figures unavailable. {error}</p>}
    {data && figures && <>
      <section aria-label="Sync status" className="mb-5 rounded-lg border bg-white p-4 text-sm">
        <h2 className="mb-2 font-semibold">↻ Loyverse receipt import</h2>
        <p>Last successful Loyverse sync: <strong>{figures.lastSuccess ? time(figures.lastSuccess) : "No successful sync recorded"}</strong></p>
        <p className="mt-1 text-gray-600">{figures.importedReceipts} imported receipts · Dashboard updated {time(data.generatedAt)}</p>
        <p className="mt-1 text-gray-600">{data.sync.automatic ? `Automatic checks about every ${Math.round(data.sync.intervalSeconds / 60)} minutes while the server is awake. Free hosting can sleep.` : "Automatic syncing is disabled."}</p>
        {!data.sync.configured && <p className="mt-2 text-amber-900">Loyverse connection needs owner setup.</p>}
        {stale && <p className="mt-2 font-medium text-amber-900">Sync is overdue or has never completed. Recent transactions may be missing.</p>}
        {data.sync.error && <p role="alert" className="mt-2 text-red-800">Sync error: {data.sync.error}</p>}
      </section>
      <section aria-labelledby="period-heading">
        <h2 id="period-heading" className="text-lg font-semibold">{data.from} to {data.to}</h2>
        <p className="mb-3 mt-1 text-sm text-gray-600">Sales use the transaction date. Collections use the date money was received.</p>
        <dl className="dashboard-stats">
          <Card label="Imported sales" value={money(figures.sales)} detail="By transaction date · payment status may still need review." />
          <Card label="Recorded collections" value={money(figures.cash + figures.gcash)} detail="By actual payment date, not the POS Cash label." highlight />
          <Card label="Confirmed unpaid" value={money(figures.current.outstanding)} detail={`${figures.current.unpaidReceipts} outstanding receipts · all dates, current balances.`} />
          <Card label="Needs review" value={figures.reviewReceipts} detail="Sale receipts in the selected period with unverified payment status or source changes." />
        </dl>
        {(figures.reviewReceipts > 0 || figures.excludedReceipts > 0 || figures.flaggedCollections > 0) && <div className="my-3 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950">
          <p><strong>{figures.reviewReceipts} sale receipts need review</strong> in this transaction-date range. Unknown collections are not estimated.</p>
          {figures.changedSalesAmount > 0 && <p className="mt-1">Changed receipts totalling {money(figures.changedSalesAmount)} in original sales are excluded pending review.</p>}
          {figures.excludedReceipts > 0 && <p className="mt-1">{figures.excludedReceipts} cancelled/refund/other receipts excluded from sales.</p>}
          {figures.flaggedCollections > 0 && <p className="mt-2 font-medium">{money(figures.flaggedCollections)} of recorded collections is linked to receipts needing reconciliation. It remains included because money was recorded as received.</p>}
          <Link className="mt-2 inline-block font-medium underline" to="/loyverse">Review receipts and payment evidence</Link>
        </div>}
        {figures.periodReceipts === 0 && <p className="my-3 text-sm">No imported receipts have a transaction date in this period. Collections can still include payments for older receipts.</p>}
      </section>
      <div className="dashboard-columns mt-5">
        <section className="dashboard-panel" aria-labelledby="chart-heading"><h2 id="chart-heading">Sales vs. collections</h2><p className="dashboard-description">Selected dates · PHP · days with included activity</p>
          {chartDays.length === 0 ? <p className="my-6 text-sm text-gray-600">No included activity for these dates.</p> : <div className="dashboard-chart-scroll"><div className="dashboard-chart" style={{ minWidth: Math.max(240, chartDays.length * 42) }} role="img" aria-label="Sales and recorded collections by date. Exact amounts are in the daily breakdown below.">
            {chartDays.map(day => <div className="dashboard-chart-day" key={day.date} title={`${day.date}: sales ${money(day.sales)}, collections ${money(day.cash + day.gcash)}`}><div className="dashboard-bars"><span className="dashboard-bar" style={{ height: `${day.sales / chartMax * 100}%` }} /><span className="dashboard-bar dashboard-bar-collected" style={{ height: `${(day.cash + day.gcash) / chartMax * 100}%` }} /></div><span>{day.date.slice(5)}</span></div>)}
          </div></div>}
          <p className="dashboard-legend"><span><i /> Sales</span><span><i className="collected" /> Recorded collections</span></p>
        </section>
        <section className="dashboard-panel" aria-labelledby="methods-heading"><h2 id="methods-heading">Where collections came from</h2><p className="dashboard-description">Only payments recorded as received · selected dates</p>
          <dl><div className="dashboard-payment-line"><dt>Cash</dt><dd>{money(figures.cash)}</dd></div><div className="dashboard-payment-line"><dt>GCash</dt><dd>{money(figures.gcash)}</dd></div><div className="dashboard-payment-line font-semibold"><dt>Recorded collections</dt><dd>{money(figures.cash + figures.gcash)}</dd></div></dl>
          <p className="dashboard-description">Not yet reconciled against the cash drawer or GCash statement. Unknown collections are not estimated.</p>
        </section>
      </div>
      <div className="dashboard-columns mt-5">
        <section className="dashboard-panel" aria-labelledby="unpaid-list-heading"><div className="dashboard-panel-heading"><div><h2 id="unpaid-list-heading">Outstanding customers</h2><p className="dashboard-description">Oldest confirmed unpaid or partial receipts · all dates</p></div><Link to="/loyverse?unpaid=true">View all →</Link></div>
          {figures.unpaidPreview.length === 0 ? <p className="my-4 text-sm text-gray-600">No confirmed outstanding balances. Unverified receipts are separate.</p> : figures.unpaidPreview.map(receipt => <div className="dashboard-receipt" key={receipt.receiptNumber}><div><strong>{receipt.customer || "Customer not linked"}</strong><span className="dashboard-chip">{receipt.partial ? "PARTIAL" : "UNPAID"}</span><p>#{receipt.receiptNumber} · {receipt.date}</p></div><div className="text-right"><strong>{money(receipt.balance)}</strong><Link className="dashboard-small-button" to={`/loyverse?receipt=${encodeURIComponent(receipt.receiptNumber)}`}>Record payment</Link></div></div>)}
        </section>
        <section className="dashboard-panel" aria-labelledby="review-list-heading"><div className="dashboard-panel-heading"><div><h2 id="review-list-heading">Needs your attention</h2><p className="dashboard-description">Sale receipts needing review · selected dates</p></div><Link to="/loyverse?unpaid=true">View receipts →</Link></div>
          {figures.reviewPreview.length === 0 ? <p className="my-4 text-sm text-gray-600">No sale receipts need review in these dates. Cancelled receipts and refunds remain separate.</p> : figures.reviewPreview.map(receipt => <div className="dashboard-receipt" key={receipt.receiptNumber}><div><strong>#{receipt.receiptNumber} · {receipt.customer || "Customer not linked"}</strong><p>{receipt.note || "No receipt comment"}</p></div><Link className="dashboard-small-button" to={`/loyverse?receipt=${encodeURIComponent(receipt.receiptNumber)}`}>Review</Link></div>)}
        </section>
      </div>
      <section aria-labelledby="balances-heading" className="mt-6">
        <h2 id="balances-heading" className="text-lg font-semibold">Current unpaid balances</h2>
        <p className="mb-3 mt-1 text-sm text-gray-600">All imported dates, using all payments recorded so far. These figures do not change with the date filter.</p>
        <p className="rounded-xl border bg-white p-4 text-sm leading-relaxed"><strong>{figures.current.unpaidCustomers} unpaid customers</strong> linked in Loyverse · {figures.current.unlinkedUnpaidReceipts} unpaid receipts without a customer link.<br /><strong>{figures.current.needsReview} unverified sale balances</strong> are excluded from confirmed unpaid until reviewed.</p>
      </section>
      <section aria-labelledby="daily-heading" className="mt-6 rounded-xl border bg-white p-4">
        <h2 id="daily-heading" className="mb-3 text-lg font-semibold">Daily breakdown</h2>
        {figures.days.length === 0 ? <p className="text-sm text-gray-600">No included sales or recorded collections for these dates.</p> : <div className="overflow-x-auto">
          <table className="w-full text-left text-sm"><caption className="sr-only">Sales by transaction date and collections by payment date</caption><thead><tr className="border-b"><th scope="col" className="py-2 pr-4">Date</th><th scope="col" className="p-2 text-right">Sales</th><th scope="col" className="p-2 text-right">Cash</th><th scope="col" className="p-2 text-right">GCash</th></tr></thead>
            <tbody>{figures.days.map(day => <tr key={day.date} className="border-b last:border-0"><th scope="row" className="whitespace-nowrap py-3 pr-4 font-normal">{day.date}{day.flaggedCollections > 0 && <span className="block text-xs text-amber-900">Collections need review</span>}</th><td className="whitespace-nowrap p-2 text-right">{money(day.sales)}</td><td className="whitespace-nowrap p-2 text-right">{money(day.cash)}</td><td className="whitespace-nowrap p-2 text-right">{money(day.gcash)}</td></tr>)}</tbody>
          </table></div>}
      </section>
      <p className="my-5 rounded-xl border bg-gray-100 p-4 text-sm text-gray-600"><strong>No profit number yet.</strong> Expenses, bills and loan principal/interest need to be supported first.</p>
      <p className="my-5 text-sm text-gray-600">Scope: imported Loyverse receipts and payments recorded in this app. Older manual receipts, unrecorded collections, refund payouts and expenses are not included. These figures have not been reconciled against the cash drawer or GCash statement.</p>
    </>}
  </main>;
}
