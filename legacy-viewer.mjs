import { createSession } from "./frontend/src/lib/session.mjs";

const settings = window.RALABA_CONFIG ?? {};
const session = createSession({ url: settings.supabaseUrl, key: settings.supabasePublishableKey });
const form = document.getElementById("login");
const logout = document.getElementById("logout");
const message = document.getElementById("message");
const data = document.getElementById("data");
session.subscribe(() => {
  const signedIn = Boolean(session.token());
  form.hidden = signedIn; logout.hidden = !signedIn;
  if (!signedIn) { data.replaceChildren(); message.textContent = "Sign in to view receipts."; }
});
setInterval(() => { session.token(); }, 1000);

form.addEventListener("submit", async event => {
  event.preventDefault();
  const button = document.getElementById("submit");
  const password = document.getElementById("password");
  button.disabled = true;
  try {
    await session.signIn(document.getElementById("email").value, password.value);
    const current = session.token();
    const response = await fetch(`${settings.apiBaseUrl.replace(/\/$/, "")}/receipts`, {
      headers: { Authorization: `Bearer ${current}` },
    });
    if (response.status === 401) { session.clear(); throw new Error("Please sign in again."); }
    if (response.status === 403) throw new Error("Your account does not have staff access.");
    if (!response.ok) throw new Error("Could not load receipts.");
    const receipts = await response.json();
    if (session.token() !== current) return;
    data.replaceChildren();
    for (const receipt of receipts) {
      const card = document.createElement("div");
      card.style.cssText = "border:1px solid #ccc; margin:8px; padding:8px;";
      const name = document.createElement("b");
      name.textContent = receipt.customer ?? "Walk-in";
      const amount = document.createElement("p");
      amount.textContent = `₱${receipt.amount} - ${receipt.paymentMethod}`;
      const date = document.createElement("p");
      date.textContent = new Date(receipt.date).toLocaleString();
      card.append(name, amount, date); data.append(card);
    }
    message.textContent = receipts.length ? "" : "No receipts found.";
  } catch (error) { message.textContent = error.message ?? "Unable to sign in."; }
  finally { password.value = ""; button.disabled = false; }
});
logout.addEventListener("click", () => {
  void session.signOut().catch(error => { message.textContent = error.message; });
});
