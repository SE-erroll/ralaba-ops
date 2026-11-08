import { createSession } from "./session.mjs";

export const auth = createSession({
  url: import.meta.env.VITE_SUPABASE_URL ?? "",
  key: import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY ?? "",
});
