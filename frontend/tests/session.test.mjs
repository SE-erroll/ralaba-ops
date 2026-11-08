import test from "node:test";
import assert from "node:assert/strict";
import { createSession } from "../src/lib/session.mjs";

const defaults = { url: "https://test.supabase.co", key: "sb_publishable_test" };
const success = () => Response.json({ access_token: "verified-token", expires_in: 60, refresh_token: "not-retained" });

test("starts signed out and accepts only a successful session", async () => {
  const auth = createSession({ ...defaults, fetcher: async (url, init) => {
    assert.equal(url, "https://test.supabase.co/auth/v1/token?grant_type=password");
    assert.deepEqual(JSON.parse(init.body), { email: "staff@example.test", password: "test-password" });
    return success();
  }});
  assert.equal(auth.token(), null);
  await auth.signIn("staff@example.test", "test-password");
  assert.equal(auth.token(), "verified-token");
});

test("expires sessions and notifies subscribers so receipts are removed", async () => {
  let time = 0; let notifications = 0;
  const auth = createSession({ ...defaults, now: () => time, fetcher: async () => success() });
  auth.subscribe(() => { notifications++; });
  await auth.signIn("staff", "password");
  time = 60000;
  assert.equal(auth.token(), null);
  assert.equal(notifications, 3);
});

test("rejects bad credentials without exposing upstream messages", async () => {
  const auth = createSession({ ...defaults, fetcher: async () => Response.json({ error: "private-detail" }, { status: 400 }) });
  await assert.rejects(auth.signIn("staff", "bad"), /Check your email and password/);
  assert.equal(auth.token(), null);
});

test("logout clears local access even when server logout fails", async () => {
  const auth = createSession({ ...defaults, fetcher: async url => {
    if (url.includes("/logout")) throw new TypeError("offline");
    return success();
  }});
  await auth.signIn("staff", "password");
  await assert.rejects(auth.signOut(), /Signed out on this browser/);
  assert.equal(auth.token(), null);
});

test("rejects unsafe Auth URLs and secret keys before sending credentials", async () => {
  for (const overrides of [{url: "http://test.supabase.co"}, {url: "https://test.supabase.co/path"},
    {key: "sb_secret_never-use"}, {key: ""}, {url: ""}]) {
    const auth = createSession({ ...defaults, ...overrides, fetcher: () => { assert.fail("No network allowed"); } });
    await assert.rejects(auth.signIn("staff", "password"));
    assert.equal(auth.token(), null);
  }
});

test("rejects malformed session responses", async () => {
  for (const body of [{}, {access_token: "token", expires_in: -1}, {access_token: 4, expires_in: 60}]) {
    const auth = createSession({ ...defaults, fetcher: async () => Response.json(body) });
    await assert.rejects(auth.signIn("staff", "password"), /invalid session/);
    assert.equal(auth.token(), null);
  }
});

test("a delayed sign-in cannot restore a cleared session", async () => {
  let finish;
  const auth = createSession({ ...defaults, fetcher: () => new Promise(resolve => { finish = resolve; }) });
  const pending = auth.signIn("staff", "password");
  auth.clear();
  finish(success());
  await pending;
  assert.equal(auth.token(), null);
});
