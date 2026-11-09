import { test, expect, type Page } from "@playwright/test";

function sample() {
  return {
    from: "2020-11-04", to: "2020-11-07", generatedAt: new Date().toISOString(),
    figures: {
      sales: 1445, cash: 585, gcash: 385, flaggedCollections: 100,
      reviewReceipts: 2, changedSalesAmount: 265, excludedReceipts: 1, periodReceipts: 5,
      current: { outstanding: 1125, unpaidReceipts: 4, unpaidCustomers: 3, unlinkedUnpaidReceipts: 1, needsReview: 2 },
      unpaidPreview: [{ receiptNumber: "old-101", customer: "Sample customer", date: "2020-11-04", balance: 265, partial: false }],
      reviewPreview: [{ receiptNumber: "review-1", customer: "Sample review", date: "2020-11-05", note: "Not clear" }],
      days: [ {date:"2020-11-07",sales:0,cash:585,gcash:385,flaggedCollections:100}, {date:"2020-11-04",sales:1445,cash:0,gcash:0,flaggedCollections:0} ],
      lastSuccess: "2020-11-07T00:00:00Z", importedReceipts: 109,
    },
    sync: { configured:true,automatic:true,intervalSeconds:300,error:null },
  };
}
async function signIn(page: Page) {
  await page.route("https://test.supabase.co/**", route => route.fulfill({json:{access_token:"fake-test-token",expires_in:3600}}));
  await page.goto("/");
  await page.getByLabel("Email",{exact:true}).fill("test@example.test");
  await page.getByLabel("Password",{exact:true}).fill("test-only");
  await page.getByRole("button",{name:"Sign in",exact:true}).click();
}

test("mobile dashboard matches API figures, labels uncertainty and has no page overflow", async ({page},testInfo) => {
  await page.setViewportSize({width:390,height:844});
  await page.route("**/mock-api/dashboard?**",async route => {
    expect(route.request().headers().authorization).toBe("Bearer fake-test-token");
    await route.fulfill({json:sample()});
  });
  await signIn(page);
  await expect(page.getByText("₱1,445.00").first()).toBeVisible();
  await expect(page.getByText("₱970.00").first()).toBeVisible();
  await expect(page.getByText("Sync is overdue",{exact:false})).toBeVisible();
  await expect(page.getByText("₱100.00 of recorded collections",{exact:false})).toBeVisible();
  await expect(page.getByText("Current unpaid balances",{exact:true})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:testInfo.outputPath("dashboard-mobile.png"),fullPage:true});
  await page.setViewportSize({width:1366,height:900});
  await page.screenshot({path:testInfo.outputPath("dashboard-desktop.png"),fullPage:true});
  await page.getByRole("button",{name:"Sign out",exact:true}).click();
  await expect(page.getByText("₱1,445.00")).toHaveCount(0);
});

test("loading and failed requests never appear as zero collections",async({page})=>{
  let resolve!:()=>void;
  const gate=new Promise<void>(r=>{resolve=r;});
  let fail=true;
  await page.route("**/mock-api/dashboard?**",async route=>{
    await gate;
    await route.fulfill(fail?{status:503,json:{error:"Test outage"}}:{json:sample()});
  });
  await signIn(page);
  await expect(page.getByText("Loading dashboard…",{exact:true})).toBeVisible();
  await expect(page.getByText("₱0.00")).toHaveCount(0);
  resolve();
  await expect(page.getByRole("alert")).toContainText("Figures unavailable");
  await expect(page.getByText("₱0.00")).toHaveCount(0);
  fail=false;
  await page.getByRole("button",{name:"Refresh",exact:true}).click();
  await expect(page.getByText("₱970.00").first()).toBeVisible();
  fail=true;
  await page.getByRole("button",{name:"Refresh",exact:true}).click();
  await expect(page.getByRole("alert")).toContainText("Figures unavailable");
  await expect(page.getByText("₱970.00")).toHaveCount(0);
});

test("date filtering updates period figures while current balances stay labelled separately",async({page})=>{
  await page.route("**/mock-api/dashboard?**",async route=>{
    const url=new URL(route.request().url());const data=sample();
    data.from=url.searchParams.get("from")!;data.to=url.searchParams.get("to")!;
    if(data.from==="2020-11-05") {data.figures.sales=0;data.figures.cash=100;data.figures.gcash=0;data.figures.days=[];}
    await route.fulfill({json:data});
  });
  await signIn(page);
  await page.getByLabel("From",{exact:true}).fill("2020-11-05");
  await page.getByLabel("To",{exact:true}).fill("2020-11-05");
  await page.getByRole("button",{name:"Show dates"}).click();
  await expect(page.getByRole("heading",{name:"2020-11-05 to 2020-11-05"})).toBeVisible();
  await expect(page.getByText("₱100.00",{exact:true}).first()).toBeVisible();
  await expect(page.getByText("₱1,125.00",{exact:true}).first()).toBeVisible();
  await expect(page.getByText("These figures do not change with the date filter.",{exact:false})).toBeVisible();
});

test("dashboard action retrieves the selected receipt even beyond the first page",async({page})=>{
  await page.route("**/mock-api/**",async route=>{
    const url=new URL(route.request().url());
    if(url.pathname.endsWith("/dashboard")) return route.fulfill({json:sample()});
    if(url.pathname.endsWith("/receipts")) {
      expect(url.searchParams.get("number")).toBe("old-101");
      return route.fulfill({json:{rows:[],count:0}});
    }
    if(url.pathname.endsWith("/status")) return route.fulfill({json:{configured:true,automatic:true,lastSuccess:null,count:0,error:null}});
    return route.fulfill({json:{days:[],needsReview:0,scope:"Test"}});
  });
  await signIn(page);
  const selected=page.waitForRequest(r=>r.url().includes("/receipts?")&&r.url().includes("number=old-101"));
  await page.getByRole("link",{name:"Record payment",exact:true}).click();
  await selected;
  await expect(page).toHaveURL(/\/loyverse\?receipt=old-101$/);
  await expect(page.getByRole("button",{name:"Show all receipts"})).toBeVisible();
});
