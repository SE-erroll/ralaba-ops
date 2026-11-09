import { useCallback, useEffect, useState, type FormEvent } from "react";
import { request } from "../lib/api";
import { useSearchParams } from "react-router-dom";

type Payment = { id: string; amount: number; date: string; method: string; note: string; initial: boolean };
type ImportedReceipt = { receiptNumber: string; date: string; customer: string | null; total: number; received: number;
  balance: number | null; status: string; initialStatus: string; initialReceived: number | null; note: string | null;
  reviewReason: string | null; canReview: boolean; services: { name: string; quantity: number }[]; payments: Payment[] };
type Page = { rows: ImportedReceipt[]; count: number };
type Status = { configured: boolean; automatic: boolean; lastSuccess: string | null; count: number; error: string | null };
type Collections = { days: { date: string; method: string; amount: number }[]; needsReview: number; scope: string };
const money = (value: number) => new Intl.NumberFormat("en-PH", { style: "currency", currency: "PHP" }).format(value);
const localDate = (value: string) => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Manila", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(value));
const today = () => localDate(new Date().toISOString());

function PaymentForm({ receipt, onSaved }: { receipt: ImportedReceipt; onSaved: () => void }) {
  const initial = receipt.canReview;
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(initial ? localDate(receipt.date) : today());
  const [method, setMethod] = useState("CASH");
  const [note, setNote] = useState("");
  const [requestId, setRequestId] = useState(() => crypto.randomUUID());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  // Keep the same ID after uncertain network failures; retry cannot double-charge a receipt.
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError("");
    try {
      await request(`/loyverse/receipts/${encodeURIComponent(receipt.receiptNumber)}/${initial ? "initial-payment" : "payments"}`,
        { method: "POST", body: JSON.stringify({ requestId, amount: Number(amount), date, method, note }) });
      setAmount(""); setNote(""); setRequestId(crypto.randomUUID()); onSaved();
    } catch (err) { setError(err instanceof Error ? err.message : "Payment was not confirmed. Retry with the same details."); }
    finally { setBusy(false); }
  }
  return <details className="mt-3 border-t pt-2"><summary className="cursor-pointer font-medium">{initial ? "Confirm initial payment" : "Record later payment"}</summary>
    <p className="my-2 text-sm">{initial ? "Check the original logbook or Excel. Enter 0 if nothing was collected when the receipt was issued. Record later collections separately." : "Only record money actually received. Do not re-enter payments already shown below."}</p>
    <form onSubmit={submit} className="grid gap-2">
      <label>Amount received<input className="block w-full rounded border p-2" type="number" step="0.01" min={initial ? 0 : 0.01} max={initial ? receipt.total : receipt.balance ?? 0} required value={amount} onChange={e => setAmount(e.target.value)} disabled={busy} /></label>
      <label>Collection date (Philippines)<input className="block rounded border p-2" type="date" min={localDate(receipt.date)} max={today()} required value={date} onChange={e => setDate(e.target.value)} disabled={initial || busy} /></label>
      <label>Payment method<select className="block rounded border p-2" value={method} onChange={e => setMethod(e.target.value)} disabled={busy}><option value="CASH">Cash</option><option value="GCASH">GCash</option></select></label>
      <label>{initial ? "Logbook / Excel evidence" : "Reference or note"}<input className="block w-full rounded border p-2" maxLength={500} required={initial} value={note} onChange={e => setNote(e.target.value)} disabled={busy} /></label>
      <button className="rounded bg-blue-700 p-2 text-white disabled:opacity-50" disabled={busy}>{busy ? "Saving…" : initial ? "Confirm initial payment" : "Save payment"}</button>
      {error && <p role="alert" className="text-red-700">{error}</p>}
    </form>
  </details>;
}

export default function LoyversePage() {
  const [search, setSearch] = useSearchParams();
  const selectedNumber = search.get("receipt") ?? "";
  const [page, setPage] = useState<Page>({ rows: [], count: 0 });
  const [status, setStatus] = useState<Status | null>(null);
  const [collections, setCollections] = useState<Collections | null>(null);
  const [unpaid, setUnpaid] = useState(search.get("unpaid") === "true");
  const [offset, setOffset] = useState(0);
  const [from, setFrom] = useState("2026-11-04");
  const [to, setTo] = useState(today());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const load = useCallback(async () => {
    try {
      const [p, s, c] = await Promise.all([
        request<Page>(`/loyverse/receipts?offset=${offset}&unpaid=${unpaid}&number=${encodeURIComponent(selectedNumber)}`),
        request<Status>("/loyverse/status"),
        request<Collections>(`/loyverse/collections?from=${from}&to=${to}`)
      ]);
      setPage(p); setStatus(s); setCollections(c); setError("");
    } catch (err) { setError(err instanceof Error ? err.message : "Could not load Loyverse records."); }
  }, [offset, unpaid, from, to, selectedNumber]);
  useEffect(() => { let active = true; void load(); const timer = window.setInterval(() => { if (active) void load(); }, 60000); return () => { active = false; window.clearInterval(timer); }; }, [load]);
  async function sync() {
    setBusy(true); setNotice(""); setError("");
    try {
      const result = await request<{ busy: boolean; inserted?: number }>("/loyverse/sync", { method: "POST" });
      setNotice(result.busy ? "A sync is already running. The list will refresh automatically." : `Sync finished. ${result.inserted ?? 0} new receipts.`);
      await load();
    } catch (err) { setError(err instanceof Error ? err.message : "Sync could not finish."); }
    finally { setBusy(false); }
  }
  const groups = new Map<string, ImportedReceipt[]>();
  for (const receipt of page.rows) { const day = localDate(receipt.date); groups.set(day, [...(groups.get(day) ?? []), receipt]); }
  return <main className="mx-auto max-w-6xl p-4 sm:p-6">
    <h1 className="text-2xl font-semibold">Loyverse receipts & payments</h1>
    {selectedNumber && <p className="my-3">Receipt #{selectedNumber} · <button className="text-blue-700 underline" onClick={() => { setSearch({}); setOffset(0); }}>Show all receipts</button></p>}
    <p className="mt-2 text-sm">Comments: PAID · UNPAID · PARTIAL 100 CASH (or GCASH). Later payments stay here; Loyverse is unchanged.</p>
    <div className="my-4 flex flex-wrap items-center gap-3"><button className="rounded bg-blue-700 px-4 py-2 text-white disabled:opacity-50" onClick={() => void sync()} disabled={busy}>{busy ? "Syncing…" : "Sync now"}</button>
      <button className="rounded border px-4 py-2" onClick={() => void load()}>Refresh</button>
      <span className="text-sm">Last completed sync: {status?.lastSuccess ? new Date(status.lastSuccess).toLocaleString("en-PH", { timeZone: "Asia/Manila" }) : "Not yet"} · {status?.count ?? 0} receipts</span></div>
    <p className="text-sm text-gray-600">{status?.automatic ? "Checks every 5 minutes while the server is awake; catches up after it wakes." : "Automatic syncing is not enabled yet."} Free hosting may sleep when the app is closed.</p>
    {!status?.configured && status && <p className="mt-2 text-amber-800">Owner setup needed: save the Loyverse token in Render.</p>}
    {(error || status?.error) && <p role="alert" className="my-3 text-red-700">{error || status?.error}</p>}
    {notice && <p role="status" className="my-3">{notice}</p>}
    <section className="my-6 rounded border bg-white p-4"><h2 className="text-lg font-semibold">Confirmed collections</h2>
      <div className="my-3 flex flex-wrap gap-4"><label>From <input className="rounded border p-1" type="date" value={from} onChange={e => setFrom(e.target.value)} /></label><label>To <input className="rounded border p-1" type="date" value={to} onChange={e => setTo(e.target.value)} /></label></div>
      <p className="text-sm">{collections?.scope}</p>
      {!!collections?.needsReview && <p className="my-2 text-amber-800">{collections.needsReview} receipts need review. These totals are incomplete until reconciled.</p>}
      <p className="my-3 text-xl font-semibold">{money(collections?.days.reduce((sum, day) => sum + day.amount, 0) ?? 0)}</p>
      <table className="w-full text-left"><thead><tr><th>Date collected</th><th>Method</th><th>Amount</th></tr></thead><tbody>{collections?.days.map(day => <tr key={`${day.date}-${day.method}`}><td>{day.date}</td><td>{day.method}</td><td>{money(day.amount)}</td></tr>)}</tbody></table>
    </section>
    <label className="flex items-center gap-2"><input type="checkbox" checked={unpaid} onChange={e => { setUnpaid(e.target.checked); setOffset(0); }} />Show unpaid, partial and needs-review receipts only</label>
    {page.rows.length === 0 && <p className="my-6">No receipts in this view. Use Sync now to fetch Loyverse receipts from November 4 onward.</p>}
    {[...groups].map(([date, receipts]) => <section key={date} className="mt-6"><h2 className="mb-3 text-lg font-semibold">Transaction date: {date}</h2><div className="grid gap-4 md:grid-cols-2">{receipts.map(receipt => <article key={receipt.receiptNumber} className="rounded border bg-white p-4">
      <h3 className="font-semibold">{receipt.customer || "Customer not linked in Loyverse"} · #{receipt.receiptNumber}</h3>
      <p className="my-2 font-medium">{receipt.status.replace(/_/g, " ")} · {money(receipt.total)}</p>
      <p>Collected: {money(receipt.received)} · Balance: {receipt.balance === null ? "Needs review" : money(receipt.balance)}</p>
      <p className="mt-1 text-sm">Initial: {receipt.initialStatus.replace(/_/g, " ")}{receipt.initialReceived !== null ? ` (${money(receipt.initialReceived)})` : ""}</p>
      <p className="mt-2 text-sm">Receipt comment: {receipt.note || "None"}</p>
      <ul className="my-2 text-sm">{receipt.services.map((service, i) => <li key={i}>{service.quantity} × {service.name || "Service"}</li>)}</ul>
      {receipt.reviewReason && <p className="my-2 text-sm text-amber-800">{receipt.reviewReason}</p>}
      {!!receipt.payments.length && <details className="mt-2"><summary>Payment history ({receipt.payments.length})</summary><ul>{receipt.payments.map(payment => <li className="my-2 text-sm" key={payment.id}>{payment.date} · {money(payment.amount)} · {payment.method} · {payment.initial ? "Initial" : "Later payment"}{payment.note ? ` · ${payment.note}` : ""}</li>)}</ul></details>}
      {(receipt.canReview || (receipt.balance !== null && receipt.balance > 0)) && <PaymentForm key={`${receipt.receiptNumber}-${receipt.initialStatus}`} receipt={receipt} onSaved={() => void load()} />}
    </article>)}</div></section>)}
    <div className="my-6 flex items-center gap-4"><button className="rounded border p-2 disabled:opacity-50" disabled={offset===0} onClick={() => setOffset(Math.max(0, offset-100))}>Previous</button><span>{offset+1}–{offset+page.rows.length} of {page.count}</span><button className="rounded border p-2 disabled:opacity-50" disabled={offset+100>=page.count} onClick={() => setOffset(offset+100)}>Next</button></div>
  </main>;
}
