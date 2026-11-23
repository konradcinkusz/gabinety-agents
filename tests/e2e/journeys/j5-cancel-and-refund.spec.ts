import { expect, test } from "../harness/fixtures";
import { restoreClock } from "../harness/clock";
import { resetAndReseed } from "../harness/demo-data";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { BookingDetailPage } from "../harness/pages-bookings";
import { bff, bookAndPay, getBooking, orderTask, publicProfile, registerDemoUser, uniqueDemoName } from "../harness/booking-support";
import { waitForTasksDone } from "../harness/booking-api";

// J5 (SPEC 10, rewritten for packages in D-02): a client cancels a package before the first use and gets the full refund (PLAN 2.5, A-03); once a unit is used the cancel button is gone,
// and the service refuses a cancel sent by other means. There is no 24 h rule any more: before the first use the refund is always the whole price.
// Set-up (a fresh client, a paid package, a task) goes straight to the services; the cancel itself and what the client then sees are driven through the UI,
// and the follow-up state (status, payment, units) is read from Booking's API.
test.describe("J5 cancel: full refund before the first use, no cancel after", () => {
  const SLUG = "demo-bartosz-testowy";

  test.describe.configure({ retries: 0 }); // a retry would only repeat the 6 minute clean-up below
  let failed = false;

  // CI diagnostics: the list reporter prints failure details only at the very end of the run, so say why a test failed as it happens (assertion text only).
  test.afterEach(async ({}, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus) {
      failed = true;
      console.log(`[${testInfo.title}] ${(testInfo.error?.message ?? "failed").split("\n").slice(0, 12).join(" | ")}`);
    }
  });

  // Every row this journey writes is unique to it (a fresh `demo-` client and its packages). CI runs on a fresh stack, so the 1 to 6 minute reseed
  // only runs on request, for a local re-run: E2E_RESEED=1 (README).
  test.afterAll(async () => {
    test.setTimeout(11 * 60_000);
    await restoreClock();
    if (process.env.E2E_RESEED === "1" && !failed) await resetAndReseed();
  });

  test("cancelling before the first use shows the full refund and the payment is Refunded", async ({ page, context }) => {
    const profile = await publicProfile(SLUG);
    const offering = profile.offerings.find((o) => o.kind === "tasks" && o.units === 5)!; // a pack: the cancel is not about the last unit
    await registerDemoUser(context, uniqueDemoName("j5-early"));
    const b = await bookAndPay(context, { offeringId: offering.id });

    const detail = new BookingDetailPage(page);
    await detail.goto(b.id);
    await expect(detail.value("Aktywny")).toBeVisible();
    await expect(detail.value("Opłacona")).toBeVisible();
    await expect(detail.usage()).toHaveText("wykorzystano 0 z 5");
    await expectNoSeriousA11yViolations(page);

    await detail.cancelButton().click();
    // UX-031 (audit P-BKS-2, F-11): the dialog states the outcome for THIS package, not the general rule; the outcome below is what proves it
    await expect(detail.cancelDialog()).toContainText("Otrzymasz zwrot");
    await detail.confirmCancelAndWait();
    await expect(detail.value(/^Zrezygnowano$/)).toBeVisible();
    await expect(detail.value("Zwrócona w całości")).toBeVisible();
    const m = offering.price.amountMinor; // below 1000 zł: no thousands separator
    await expect(detail.value(/Zwrócono:/)).toContainText(`${Math.floor(m / 100)},${String(m % 100).padStart(2, "0")}`);
    await expect(detail.cancelButton()).toHaveCount(0); // a cancelled package is not cancelled twice
    await expectNoSeriousA11yViolations(page);

    await expect.poll(async () => (await getBooking(context, b.id)).payment.status, { message: "payment of the cancel" }).toBe("Refunded");
    const view = await getBooking(context, b.id);
    expect(view.status).toBe("CancelledByClient");
    expect(view.payment.refunded.amountMinor).toBe(offering.price.amountMinor); // 100 %, CONTRACTS 9.2
    expect(view.unitsUsed).toBe(0);

    // a cancelled package takes no task: the panel's route refuses it with the same invalid_transition the page words as "stan się zmienił"
    const task = await bff(context.request, "POST", `/api/proxy/booking/api/v1/bookings/${b.id}/tasks`, { body: { input: "Zadanie testowe po rezygnacji z pakietu, dane syntetyczne." } });
    expect(task.status()).toBe(409);
  });

  test("after the first use there is no cancel button and the service refuses a cancel", async ({ page, context }) => {
    const profile = await publicProfile(SLUG);
    const offering = profile.offerings.find((o) => o.kind === "tasks" && o.units === 5)!;
    await registerDemoUser(context, uniqueDemoName("j5-used"));
    const b = await bookAndPay(context, { offeringId: offering.id });

    const detail = new BookingDetailPage(page);
    await detail.goto(b.id);
    await expect(detail.cancelButton()).toBeVisible(); // nothing used yet: it is there ...
    expect((await getBooking(context, b.id)).canCancel).toBe(true);

    await orderTask(context, b.id, "Zaproponuj etapy migracji opisanej aplikacji do nowszej wersji .NET (j5).");
    // the page refreshes itself with a meta tag while a task is queued or running, and axe rejects that (meta-refresh, WCAG 2.2.1): look at it once the answer is there
    await waitForTasksDone(context.request, b.id);
    await detail.goto(b.id);
    await expect(detail.value("Aktywny")).toBeVisible(); // ... the package is still active, with units left ...
    await expect(detail.usage()).toHaveText("wykorzystano 1 z 5");
    await expect(detail.cancelButton()).toHaveCount(0); // ... but it cannot be cancelled any more
    await expect(detail.disputeButton()).toBeVisible(); // a claim is what remains (A-03)
    await expectNoSeriousA11yViolations(page);

    const view = await getBooking(context, b.id);
    expect(view.canCancel).toBe(false);
    expect(view.unitsUsed).toBe(1);
    // the service says the same when the cancel is sent without the page: 409 invalid_transition, and nothing changes
    const cancel = await bff(context.request, "POST", `/api/proxy/booking/api/v1/bookings/${b.id}/cancel`, { body: { reason: "Próba rezygnacji po użyciu pakietu." } });
    expect(cancel.status()).toBe(409);
    expect(((await cancel.json()) as { code?: string }).code).toBe("invalid_transition");
    const after = await getBooking(context, b.id);
    expect([after.status, after.payment.status, after.payment.refunded.amountMinor]).toEqual(["Confirmed", "Succeeded", 0]);
  });
});
