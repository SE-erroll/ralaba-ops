import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { auth } from "../lib/auth";

export default function StaffAccess({ children }: { children: ReactNode }) {
  const [signedIn, setSignedIn] = useState(() => Boolean(auth.token()));
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    const unsubscribe = auth.subscribe(() => setSignedIn(Boolean(auth.token())));
    const timer = window.setInterval(() => { auth.token(); }, 1000);
    return () => { unsubscribe(); window.clearInterval(timer); };
  }, []);
  async function signIn(event: FormEvent) {
    event.preventDefault(); setError(""); setBusy(true);
    try { await auth.signIn(email, password); }
    catch (err) { setError(err instanceof Error ? err.message : "Sign-in failed."); }
    finally { setPassword(""); setBusy(false); }
  }
  if (signedIn) return <>
    <header className="flex items-center justify-between border-b bg-white p-4">
      <nav className="flex flex-wrap gap-4"><Link to="/">Dashboard</Link><Link to="/receipts">Existing receipts</Link><Link to="/loyverse">Loyverse & payments</Link></nav>
      <button onClick={() => { void auth.signOut().catch(err => setError(err.message)); }}>Sign out</button>
    </header>
    {children}
  </>;
  return <main className="mx-auto max-w-sm p-6">
    <h1 className="text-2xl font-semibold">Ra Laba staff sign-in</h1>
    <form onSubmit={signIn} className="mt-4 grid gap-4">
      <label>Email<input className="mt-1 w-full rounded border p-2" type="email"
        autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} /></label>
      <label>Password<input className="mt-1 w-full rounded border p-2" type="password"
        autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} /></label>
      <button className="rounded bg-blue-700 p-2 text-white" disabled={busy}>
        {busy ? "Signing in…" : "Sign in"}
      </button>
    </form>
    {error && <p role="alert" className="mt-4 text-red-700">{error}</p>}
  </main>;
}
