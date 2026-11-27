import { test, expect } from "../harness/fixtures";
import { signInViaBff } from "../harness/auth";
import { restoreClock } from "../harness/clock";
import { urls } from "../harness/env";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import {
  reseedIfRequested, reviewAttemptStatus, bookingIdFromUrl, createBooking, createReview, getBooking, listTasks, orderTask, publicProfile, simulatePayment, waitForStatus, waitForTasksDone,
} from "../harness/booking-api";
import { BookingFormPage, ConfirmationPage, MockCheckoutPage, PractitionerProfilePage, ReviewFormPage } from "../harness/pages-booking";
import { BookingDetailPage } from "../harness/pages-bookings";

/**
 * J3 (SPEC 10, rewritten for packages in D-02): a client buys a `Tasks` package and pays on the mock, orders a task in the panel, sees the agent's result, the package is used up and
 * completes, the client reviews, the review appears and the rating shows "Nowy" until three.
 *
 * Agent: demo-celina-przykladowska (demo-practitioner-3, the seeded profile with no real review: SPEC 7 "new"); her package is one task. Three packages are bought (the first through the UI,
 * two through the same calls the UI makes) so that the rating can be asserted at 2 reviews ("Nowy") and at 3 (the average). Everything is `demo-` data; the seeded bookings and reviews
 * of other agents are not touched. The fake agent platform answers `[demo <slug>] Otrzymano N znaków.` with N the length of the task, so the result is asserted exactly.
 */
const SLUG = "demo-celina-przykladowska";
const NAME = "Celina Przykładowska";
const PRACTITIONER_USER = "demo-practitioner-3";
const BRIEFS = [
  "Chcę sprawdzić, jak agent poradzi sobie z architekturą prostego prototypu z modelem językowym (j3-1).",
  "Chcę ocenić koszty i ryzyka integracji modelu językowego z aplikacją (j3-2).",
  "Proszę o przegląd sposobu oceny jakości odpowiedzi modelu językowego (j3-3).",
];
const TASKS = [
  "Zaproponuj architekturę prostego prototypu wyszukiwania w dokumentach firmowych z użyciem modelu językowego (j3-1).",
  "Oszacuj koszty i ryzyka integracji modelu językowego z istniejącą aplikacją obsługi klienta (j3-2).",
  "Opisz sposób oceny jakości odpowiedzi modelu językowego na zbiorze pytań z obsługi klienta (j3-3).",
];
const answerTo = (task: string) => `[demo ${SLUG}] Otrzymano ${task.length} znaków.`;
const REVIEWS = [
  { rating: 5, text: "Agent rzetelnie opisał architekturę, dostałem konkretny plan prototypu (j3-1)." },
  { rating: 4, text: "Dobry wynik zadania o kosztach i ryzykach, kilka praktycznych wskazówek (j3-2)." },
  { rating: 3, text: "Pomocne uwagi o ocenie jakości, trochę zabrakło szczegółów w wyniku (j3-3)." },
];
const AVERAGE = (5 + 4 + 3) / 3;

test.describe("J3 buy a task package, order a task, see the result, review", () => {
  // No retry: a retry meets the data the failed attempt left (J3 needs a profile with no review).
  test.describe.configure({ mode: "serial", retries: 0 });
  // The clock is global to the fake profile; this journey does not move it, but a failed run must leave it at the baseline anyway. Then remove the demo rows this journey wrote (SPEC 7).
  test.afterAll(async () => {
    test.setTimeout(900_000); // E2E_RESEED=1 only: the reseed takes 1 to 6 minutes, the default hook timeout is 60 s
    await restoreClock();
    await reseedIfRequested();
  });

  test("a client buys a package and pays, orders a task and reads the result, the package completes, three reviews turn \"Nowy\" into an average", async ({ page, context, browser, signInAs }) => {
    test.setTimeout(420_000); // one turn of Booking's 60 s auto-complete job, the task job (5 s) and the UI steps
    await signInAs("demo-client");
    const client = context.request;

    // ---- the starting point: a profile with no review shows "Nowy" ----
    const before = await publicProfile(SLUG);
    expect(before.isNew, "start from the seeded profile with no reviews (reset the demo data first)").toBe(true);
    expect(before.rating ?? null).toBeNull();
    const offering = before.offerings.find((o) => o.kind === "tasks")!;
    expect(offering, "the agent sells a task package").toBeDefined();
    const price = offering.price.amountMinor;

    const profile = new PractitionerProfilePage(page);
    await profile.goto(SLUG, NAME);
    await expect(profile.newBadge()).toBeVisible();
    await expect(profile.aiBadge()).toBeVisible();
    await expect(profile.offeringFacts(offering.title)).toContainText("1 zadanie, typowy czas ok. 60 min");
    await expectNoSeriousA11yViolations(page);

    // ---- 1. buy and pay through the UI: no date to choose ----
    await profile.openBookingForm(await profile.purchaseLink(offering.title));
    const form = new BookingFormPage(page);
    await expect(page.getByRole("heading", { level: 1, name: "Wykup dostępu" })).toBeVisible();
    await expect(page.getByText(offering.title)).toBeVisible();
    await expect(page.getByText(/termin|godzin/i)).toHaveCount(0);
    await expectNoSeriousA11yViolations(page);
    const bookingIds: string[] = [await form.submit(BRIEFS[0]!)];

    let view = await getBooking(client, bookingIds[0]!);
    expect(view.status).toBe("PendingPayment");
    expect(view.payment.status).toBe("Created");
    await expect(page.getByRole("heading", { level: 1, name: "Płatność (tryb demonstracyjny)" })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await new MockCheckoutPage(page).paySuccessfully();
    expect(bookingIdFromUrl(page.url())).toBe(bookingIds[0]);
    await expectNoSeriousA11yViolations(page);

    view = await getBooking(client, bookingIds[0]!);
    expect(view.status).toBe("Confirmed");
    expect(view.payment.status).toBe("Succeeded");
    expect(view.price.amountMinor).toBe(price);
    // a purchase has no chosen slot: the package runs from the purchase for 30 days, with one unit and nothing used yet
    expect(new Date(view.endUtc).getTime() - new Date(view.startUtc).getTime()).toBe(30 * 24 * 3_600_000);
    expect([view.kind, view.unitsTotal, view.unitsUsed]).toEqual(["Tasks", 1, 0]);
    expect(view.canCancel).toBe(true); // nothing used yet: a full refund is still possible

    // ---- 2. the provider sees the package (their own session, a second browser context) ----
    const pctx = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
    try {
      await signInViaBff(pctx, PRACTITIONER_USER);
      const pp = await pctx.newPage();
      // the API first: the provider's own view of the package (status, payment, the brief that only a Confirmed package shows them)
      const seen = await getBooking(pctx.request, bookingIds[0]!);
      expect(seen.status).toBe("Confirmed");
      expect(seen.payment.status).toBe("Succeeded");
      expect(seen.brief?.text).toBe(BRIEFS[0]);
      await pp.goto("/moje/dostepy?role=practitioner");
      await expect(pp.getByRole("listitem").filter({ hasText: "Aktywny" }).filter({ hasText: offering.title })).toHaveCount(1);
      await expectNoSeriousA11yViolations(pp);
    } finally {
      await pctx.close();
    }

    // ---- 3. the confirmation leads to the first task: ordered in the panel, answered by the agent ----
    await page.goto(`/dostep/potwierdzenie/${bookingIds[0]}`);
    const confirmation = new ConfirmationPage(page);
    await expect(confirmation.heading()).toBeVisible();
    await expect(confirmation.createKeyLink()).toBeVisible();
    await confirmation.firstTaskLink().click();
    await page.waitForURL(new RegExp(`/moje/dostepy/${bookingIds[0]}`));

    const detail = new BookingDetailPage(page);
    await expect(detail.usage()).toHaveText("wykorzystano 0 z 1");
    await expect(detail.cancelButton()).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await detail.orderTask(TASKS[0]!);
    // the unit is taken at once: the form is gone, the usage shows it, and a used package can no longer be cancelled
    await expect(detail.usage()).toHaveText("wykorzystano 1 z 1");
    await expect(detail.taskSubmit()).toHaveCount(0);
    await expect(detail.cancelButton()).toHaveCount(0);
    // the task is listed at once, with the date it was ordered (not an empty one); the fake platform's answer is deterministic
    await expect(detail.tasks()).toHaveCount(1);
    await expect(detail.tasks().first()).toContainText(/Zlecone: \d{1,2} \p{L}+ 20\d\d/u);
    await expectNoSeriousA11yViolations(page); // the list as it is right after the order (the task is usually still queued): no meta refresh any more, so axe may look at it
    await detail.waitForAnswer(answerTo(TASKS[0]!));
    await expect(detail.tasks()).toHaveCount(1);
    await expect(detail.tasks().first()).toContainText(answerTo(TASKS[0]!));
    await expect(detail.tasks().first()).toContainText("Gotowe");
    await expectNoSeriousA11yViolations(page);
    const done = await waitForTasksDone(client, bookingIds[0]!);
    expect(done.map((t) => [t.status, t.output])).toEqual([["succeeded", answerTo(TASKS[0]!)]]);

    // ---- 4. two more packages, bought and paid through the same calls the UI makes, each with a task answered ----
    for (const i of [1, 2]) {
      const { bookingId } = await createBooking(client, offering.id, BRIEFS[i]!);
      await simulatePayment(client, bookingId, "succeeded");
      await orderTask(client, bookingId, TASKS[i]!);
      expect((await waitForTasksDone(client, bookingId)).map((t) => t.output)).toEqual([answerTo(TASKS[i]!)]);
      bookingIds.push(bookingId);
    }

    // ---- 5. the used-up packages complete (the auto-complete job runs every 60 s, no business time to pass) ----
    for (const id of bookingIds) await waitForStatus(client, id, "Completed");
    for (const id of bookingIds) {
      const completed = await getBooking(client, id);
      expect(completed.status).toBe("Completed");
      expect(completed.payment.status).toBe("Succeeded"); // completing does not touch the payment
      expect([completed.unitsTotal, completed.unitsUsed]).toEqual([1, 1]);
      expect(completed.canReview).toBe(true);
    }
    await detail.goto(bookingIds[0]!);
    await expect(detail.value("Zakończony")).toBeVisible();
    await expect(detail.reviewLink()).toBeVisible();

    // ---- 6. the first review through the UI: still "Nowy" ----
    const reviewForm = new ReviewFormPage(page);
    await reviewForm.goto(bookingIds[0]!);
    await expectNoSeriousA11yViolations(page);
    await reviewForm.submit(REVIEWS[0]!.rating, REVIEWS[0]!.text);
    await expect(page.getByRole("heading", { name: "Dziękujemy za opinię" })).toBeVisible();
    await expect(page.getByText("Opinia została opublikowana na profilu agenta.")).toBeVisible();
    expect(await reviewAttemptStatus(client, bookingIds[0]!, 1, REVIEWS[0]!.text)).toBe(409); // one review per package (the flag canReview does not know)

    // ---- 7. reviews 2 (still "Nowy") and 3 (the average) ----
    expect((await createReview(client, bookingIds[1]!, REVIEWS[1]!.rating, REVIEWS[1]!.text)).status).toBe("Published");
    const atTwo = await publicProfile(SLUG);
    expect(atTwo.isNew, "two reviews are not enough for a rating").toBe(true);
    expect(atTwo.rating ?? null).toBeNull();

    expect((await createReview(client, bookingIds[2]!, REVIEWS[2]!.rating, REVIEWS[2]!.text)).status).toBe("Published");
    // Reputation pushes the summary to the Directory after the review: wait for it to show.
    await expect
      .poll(async () => (await publicProfile(SLUG)).rating?.count ?? 0, { message: "the third review reaches the Directory", timeout: 60_000, intervals: [1_000, 2_000, 5_000] })
      .toBe(3);
    const atThree = await publicProfile(SLUG);
    expect(atThree.isNew).toBe(false);
    expect(atThree.rating!.average).toBeCloseTo(AVERAGE, 1);

    // ---- 8. the reviews are visible on the public page (the list is read live; the rating block of the page is cached 5 min by the web app) ----
    await page.goto(`/agent/${SLUG}`);
    const reviews = profile.reviewsSection();
    for (const r of REVIEWS) {
      await expect(reviews.getByText(r.text)).toBeVisible();
      await expect(reviews.getByText(`Ocena: ${r.rating} z 5`)).toBeVisible();
    }
    await expectNoSeriousA11yViolations(page);
    // the tasks of the three packages are still there for their owner
    expect((await listTasks(client, bookingIds[2]!)).length).toBe(1);
  });
});
