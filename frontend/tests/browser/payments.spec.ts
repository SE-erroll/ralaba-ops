import { test, expect, type Page } from "@playwright/test";

async function setup(page: Page, initial: boolean) {
  let receipt = { receiptNumber: "test-1", date: "2020-11-03T04:00:00Z", customer: "Sample customer", total: 265,
    received: initial ? 0 : 265, balance: initial ? null as number | null : 0, status: initial ? "NEEDS_REVIEW" : "PAID",
    initialStatus: initial ? "NEEDS_REVIEW" : "PAID", initialReceived: initial ? null as number | null : 265,
    note: null, reviewReason: null, canReview: initial, canCorrect: !initial, services: [],
    payments: initial ? [] as any[] : [{ id: "11111111-1111-4111-8111-111111111111", amount: 265, date: "2020-11-03", method: "CASH", note: "Logbook", initial: true, revision: 0, corrections: [] }] };
  const posts: any[] = [];
  await page.route("https://test.supabase.co/**", route => route.fulfill({ json: { access_token: "fake-test-token", expires_in: 3600 } }));
  await page.route("**/mock-api/**", async route => {
    const url = new URL(route.request().url());
    if (route.request().method() === "POST") {
      const body = route.request().postDataJSON(); posts.push({ path: url.pathname, ...body });
      if (url.pathname.endsWith("/initial-payment")) receipt = { ...receipt, initialReceived: body.amount, initialStatus: "UNPAID", canReview: false, canCorrect: true, balance: 265 };
      if (url.pathname.endsWith("/corrections")) {
        receipt = { ...receipt, received: body.amount, balance: 265 - body.amount, status: "PARTIAL", initialReceived: 0, initialStatus: "UNPAID" };
        receipt.payments[0] = { ...receipt.payments[0], amount: body.amount, date: body.date, method: body.method, initial: false, revision: 1,
          corrections: [{ before: {amount:265,collected_date:"2020-11-03",method:"CASH"}, after: {amount:body.amount,collected_date:body.date,method:body.method}, reason:body.reason, by:"test-staff", at:new Date().toISOString() }] };
      }
      return route.fulfill({ json: { saved: true } });
    }
    if (url.pathname.endsWith("/receipts")) return route.fulfill({ json: { rows: [receipt], count: 1 } });
    if (url.pathname.endsWith("/status")) return route.fulfill({ json: { configured:true,automatic:true,lastSuccess:null,count:1,error:null } });
    if (url.pathname.endsWith("/collections")) return route.fulfill({ json: { days:[],needsReview:0,scope:"Sample data only" } });
    return route.fulfill({status:503,json:{error:"Dashboard not used in this payment test"}});
  });
  await page.goto("/loyverse");
  await page.getByLabel("Email", {exact:true}).fill("test@example.test");
  await page.getByLabel("Password", {exact:true}).fill("test-only");
  await page.getByRole("button", {name:"Sign in",exact:true}).click();
  await page.getByRole("link", {name:"Loyverse & payments"}).click();
  await expect(page.getByRole("heading", {name:"Sample customer · #test-1"})).toBeVisible();
  return posts;
}

test("receipt-day confirmation explains zero, then later payment accepts its actual date", async ({page}) => {
  const posts = await setup(page, true);
  await page.getByText("1. Confirm payment on receipt date", {exact:true}).click();
  await expect(page.getByText("This step only confirms money received", {exact:false})).toBeVisible();
  await page.getByLabel("Amount received", {exact:true}).fill("0");
  await page.getByLabel("Evidence checked", {exact:true}).fill("Nov 3 logbook: unpaid at drop-off");
  await page.getByRole("button", {name:"Confirm initial payment",exact:true}).click();
  await page.getByText("Record a payment received", {exact:true}).click();
  await page.getByLabel("Amount received", {exact:true}).fill("265");
  await page.getByLabel("Actual collection date (Philippines)", {exact:true}).fill("2020-11-04");
  await page.getByRole("button", {name:"Save payment",exact:true}).click();
  await expect.poll(() => posts.length).toBe(2);
  expect(posts[0]).toMatchObject({amount:0,date:"2020-11-03"});
  expect(posts[1]).toMatchObject({amount:265,date:"2020-11-04"});
});

test("mobile correction requires confirmation and exposes before/after audit", async ({page}, testInfo) => {
  await page.setViewportSize({width:390,height:844});
  const posts = await setup(page, false);
  await page.getByText("Payment history (1)", {exact:true}).click();
  await page.getByText("Correct payment", {exact:true}).click();
  await expect(page.getByRole("button", {name:"Save correction"})).toBeDisabled();
  await page.getByLabel("Correct amount", {exact:true}).fill("200");
  await page.getByLabel("Correct collection date", {exact:true}).fill("2020-11-04");
  await page.getByRole("combobox", {name:"Correct payment method",exact:true}).selectOption("GCASH");
  await page.getByLabel("Reason and evidence", {exact:true}).fill("Checked GCash reference: received next day");
  await page.getByLabel("I checked the evidence.", {exact:false}).check();
  await page.getByRole("button", {name:"Save correction"}).click();
  await expect(page.getByText("Correction 1", {exact:false})).toBeVisible();
  await page.getByText("Correction 1", {exact:false}).click();
  await expect(page.getByText("Before: ₱265.00 · 2020-11-03 · CASH", {exact:true})).toBeVisible();
  await expect(page.getByText("After: ₱200.00 · 2020-11-04 · GCASH", {exact:true})).toBeVisible();
  await expect(page.getByText("Collected: ₱200.00 · Balance: ₱65.00", {exact:true})).toBeVisible();
  expect(posts[0]).toMatchObject({revision:0,amount:200,date:"2020-11-04",method:"GCASH"});
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({path:testInfo.outputPath("payment-correction-mobile.png"),fullPage:true});
});
