// Keep sessions in memory on shared shop browsers; never persist passwords or refresh tokens.
export function createSession({ url, key, fetcher = globalThis.fetch, now = Date.now }) {
  let session = null;
  let generation = 0;
  const listeners = new Set();
  const notify = () => listeners.forEach(listener => listener());
  const clear = () => { generation++; session = null; notify(); };
  function token() {
    if (session && session.expiresAt <= now()) clear();
    return session?.token ?? null;
  }
  function config() {
    let origin;
    try { origin = new URL(url); }
    catch { throw new Error("Staff sign-in is not configured. Please contact the owner."); }
    if (origin.protocol !== "https:" || origin.pathname !== "/" || origin.search || origin.hash
      || origin.username || origin.password || !key || key.startsWith("sb_secret_"))
      throw new Error("Staff sign-in is not configured. Please contact the owner.");
    if (key.startsWith("eyJ")) {
      try {
        const payload = JSON.parse(atob(key.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
        if (payload.role !== "anon") throw new Error();
      } catch { throw new Error("Use a public Supabase key for staff sign-in."); }
    }
    return origin.origin;
  }
  return {
    token, clear,
    subscribe(listener) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    async signIn(email, password) {
      clear();
      const attempt = generation;
      let response;
      try {
        response = await fetcher(`${config()}/auth/v1/token?grant_type=password`, {
          method: "POST", headers: { apikey: key, "Content-Type": "application/json" },
          body: JSON.stringify({ email, password }),
        });
      } catch (error) {
        if (error instanceof TypeError) throw new Error("Cannot reach sign-in. Please try again.");
        throw error;
      }
      if (!response.ok) throw new Error("Sign-in failed. Check your email and password.");
      const data = await response.json();
      if (typeof data.access_token !== "string" || !data.access_token
        || !Number.isFinite(data.expires_in) || data.expires_in <= 0)
        throw new Error("Sign-in returned an invalid session.");
      if (attempt !== generation) return;
      session = { token: data.access_token, expiresAt: now() + data.expires_in * 1000 };
      notify();
    },
    async signOut() {
      const current = token();
      clear();
      if (!current) return;
      try {
        const response = await fetcher(`${config()}/auth/v1/logout?scope=local`, {
          method: "POST", headers: { apikey: key, Authorization: `Bearer ${current}` },
        });
        if (!response.ok) throw new Error();
      } catch { throw new Error("Signed out on this browser. Server sign-out was unavailable."); }
    },
  };
}
