import test from "node:test";
import assert from "node:assert/strict";

test("legacy viewer requires sign-in, renders customer markup as text, and clears receipts on logout", async () => {
  class Element {
    children = []; handlers = {}; style = {}; value = ""; textContent = "";
    set innerHTML(_) { assert.fail("Customer content must never be parsed as HTML"); }
    addEventListener(name, callback) { this.handlers[name] = callback; }
    replaceChildren(...children) { this.children = children; }
    append(...children) { this.children.push(...children); }
  }
  const elements = Object.fromEntries(["login", "logout", "message", "data", "submit", "email", "password"]
    .map(id => [id, new Element()]));
  elements.email.value = "staff@example.test";
  elements.password.value = "test-password";
  const originals = { window: globalThis.window, document: globalThis.document,
    fetch: globalThis.fetch, setInterval: globalThis.setInterval };
  let requests = 0;
  globalThis.window = { RALABA_CONFIG: { supabaseUrl: "https://test.supabase.co",
    supabasePublishableKey: "sb_publishable_test", apiBaseUrl: "https://api.example.test/api" } };
  globalThis.document = { getElementById: id => elements[id], createElement: () => new Element() };
  globalThis.setInterval = () => 0;
  globalThis.fetch = async (url, options) => {
    requests++;
    if (url.includes("/token?")) return Response.json({ access_token: "token", expires_in: 3600 });
    if (url.includes("/logout?")) return new Response(null, { status: 204 });
    assert.equal(url, "https://api.example.test/api/receipts");
    assert.equal(options.headers.Authorization, "Bearer token");
    return Response.json([{ customer: "<img src=x onerror=alert(1)>", amount: 100,
      paymentMethod: "Cash", date: "2026-11-08T00:00:00Z" }]);
  };
  try {
    await import("../../legacy-viewer.mjs");
    assert.equal(requests, 0);
    await elements.login.handlers.submit({ preventDefault() {} });
    assert.equal(elements.data.children[0].children[0].textContent, "<img src=x onerror=alert(1)>");
    assert.equal(elements.password.value, "");
    assert.equal(elements.login.hidden, true);
    elements.logout.handlers.click();
    assert.equal(elements.data.children.length, 0);
    assert.equal(elements.login.hidden, false);
  } finally {
    Object.assign(globalThis, originals);
  }
});
