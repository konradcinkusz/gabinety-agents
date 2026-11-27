import { restoreClock } from "../harness/clock";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { chat, directModels, expectOpenAiError, models, parseSse, type Completion } from "../harness/agent-api";
import { urls } from "../harness/env";
import { expect, test } from "../harness/fixtures";
import { reseedIfRequested, bookingIdFromUrl, getBooking, publicProfile } from "../harness/booking-api";
import { BookingFormPage, ConfirmationPage, MockCheckoutPage, PractitionerProfilePage } from "../harness/pages-booking";
import { ApiKeysPage, BookingDetailPage } from "../harness/pages-bookings";
import { registerDemoUser, uniqueDemoName } from "../harness/booking-support";

/**
 * J8 (D-02, new): a client buys the Chat package of an agent, creates an API key in /moje/klucze, and a tool outside the browser (the test) calls `GET /v1/models` and
 * `POST /v1/chat/completions`, plain and streamed, with that key. The use is visible in the panel. The fake agent platform answers `[demo <slug>] Otrzymano N znaków.`
 * with N the length of the message and bills `N / 4 + 20` tokens, so the answer and the bill are asserted exactly. A fresh `demo-` client keeps the journey independent of the seeded keys and packages.
 *
 * `/v1` sits under the address of the site (ADR-0013 decision 1): the web host forwards it to Booking. The journey takes the base URL from the confirmation page (what a client is told),
 * checks it is the site's own, and makes every call through the site: the key, the stream and the errors all pass the proxy; one call is repeated against Booking to show the answer is the same.
 */
const SLUG = "demo-anna-demowska";
const NAME = "Anna Demowska";
const OTHER_AGENT = "demo-bartosz-testowy";
// a long message so that the use shows in the panel, which counts thousands of tokens: 2 x (120000 / 4 + 20) = 60040 tokens, "wykorzystano 60 z 500 tys. tokenów"
const MESSAGE = "Pytanie testowe o architekturę. ".repeat(3750); // 32 characters x 3750 = 120000
const N = MESSAGE.length;
const BILL = Math.floor(N / 4) + 20;
const ANSWER = `[demo ${SLUG}] Otrzymano ${N} znaków.`;

test.describe("J8 buy the Chat package, create a key, call the API", () => {
  test.describe.configure({ retries: 0 });
  test.afterAll(async () => {
    test.setTimeout(900_000); // E2E_RESEED=1 only: the reseed takes 1 to 6 minutes, the default hook timeout is 60 s
    await restoreClock();
    await reseedIfRequested(); // SPEC 7 cleanup, only with E2E_RESEED=1 (CI runs on a fresh stack)
  });

  test("a client buys a Chat package, makes a key, lists the models and chats, and sees the use in the panel", async ({ page, context }) => {
    test.setTimeout(240_000);
    await registerDemoUser(context, uniqueDemoName("j8"));
    const client = context.request;
    const agent = await publicProfile(SLUG);
    const offering = agent.offerings.find((o) => o.kind === "chat")!;
    expect(offering, "the agent sells a Chat package").toBeDefined();
    expect([offering.units, offering.price.amountMinor]).toEqual([500, 9900]);

    // ---- 1. buy and pay through the UI ----
    const profile = new PractitionerProfilePage(page);
    await profile.goto(SLUG, NAME);
    await expect(profile.aiBadge()).toBeVisible();
    await expect(profile.offeringFacts(offering.title)).toContainText("500 tys. tokenów, API zgodne z OpenAI");
    await profile.openBookingForm(await profile.purchaseLink(offering.title));
    const bookingId = await new BookingFormPage(page).submit("Chcę podłączyć agenta do narzędzia zgodnego z OpenAI (j8).");
    await new MockCheckoutPage(page).paySuccessfully();
    expect(bookingIdFromUrl(page.url())).toBe(bookingId);
    const bought = await getBooking(client, bookingId);
    expect([bought.status, bought.kind, bought.unitsTotal, bought.unitsUsed]).toEqual(["Confirmed", "Chat", 500_000, 0]); // the pool is held in tokens

    // ---- 2. the confirmation tells how to connect: model, base URL, and where the key is made ----
    const confirmation = new ConfirmationPage(page);
    await expect(confirmation.heading()).toBeVisible();
    await expect(confirmation.connectModel()).toHaveText(SLUG);
    const baseUrl = (await confirmation.connectBaseUrl().textContent())!.trim();
    expect(baseUrl).toMatch(/\/v1$/);
    expect(baseUrl, "the address a client is told is the site's own, plus /v1").toBe(`${urls.web}/v1`);
    await expect(confirmation.firstTaskLink()).toHaveCount(0); // a Chat package has no tasks
    await expectNoSeriousA11yViolations(page);

    // ---- 3. the key: created in the UI, shown once, kept only as a prefix ----
    await confirmation.createKeyLink().click();
    const keys = new ApiKeysPage(page);
    await page.waitForURL(/\/moje\/klucze/);
    await expect(page.getByRole("heading", { level: 1, name: "Klucze API" })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    const key = await keys.create("Moje IDE (j8)");
    expect(key).toMatch(/^gab_[A-Za-z0-9]{40}$/);
    await expectNoSeriousA11yViolations(page);
    await keys.goto(); // the list read again: the prefix, never the key
    await expect(page.getByText(key)).toHaveCount(0);
    await expect(keys.rows().filter({ hasText: "Moje IDE (j8)" })).toContainText(`gab_${key.slice(4, 12)}`);
    await expect(keys.rows().filter({ hasText: "Moje IDE (j8)" })).toContainText("jeszcze nie użyto");

    // ---- 4. GET /v1/models: this agent, OpenAI-shaped; a missing or wrong key is an OpenAI-shaped 401 ----
    expect(new URL(baseUrl).origin).toBe(new URL((await models(key)).url).origin);
    await expectOpenAiError(await models(null), 401, "invalid_api_key");
    await expectOpenAiError(await models("gab_" + "x".repeat(40)), 401, "invalid_api_key");
    const list = await models(key);
    expect(list.status).toBe(200);
    expect(await list.json()).toEqual({ object: "list", data: [{ id: SLUG, object: "model", owned_by: "gabinety" }] });
    const direct = await directModels(key); // Booking itself says the same: the site adds and removes nothing
    expect([direct.status, await direct.json()]).toEqual([200, { object: "list", data: [{ id: SLUG, object: "model", owned_by: "gabinety" }] }]);

    // ---- 5. POST /v1/chat/completions: an agent the client has no package of is 404 model_not_found ----
    await expectOpenAiError(await chat(key, OTHER_AGENT, "Pytanie do agenta bez pakietu."), 404, "model_not_found");

    // ---- 6. the plain answer: the platform's JSON as it is, with the bill ----
    const plain = await chat(key, SLUG, MESSAGE);
    expect(plain.status).toBe(200);
    const completion = (await plain.json()) as Completion;
    expect(completion.object).toBe("chat.completion");
    expect(completion.choices[0]!.message.content).toBe(ANSWER);
    expect(completion.usage.total_tokens).toBe(BILL);

    // ---- 7. the streamed answer: server-sent events, three content chunks, the usage chunk, then [DONE] ----
    const streamed = await chat(key, SLUG, MESSAGE, { stream: true });
    expect(streamed.status).toBe(200);
    expect(streamed.headers.get("content-type") ?? "").toContain("text/event-stream");
    const sse = parseSse(await streamed.text());
    expect(sse.doneIsLast).toBe(true);
    const text = sse.chunks.flatMap((c) => (c.choices as { delta?: { content?: string } }[]).map((ch) => ch.delta?.content ?? "")).join("");
    expect(text).toBe(ANSWER);
    const usage = sse.chunks.map((c) => c.usage as { total_tokens: number } | undefined).filter((u) => u !== undefined);
    expect(usage.map((u) => u!.total_tokens)).toEqual([BILL]); // only the last chunk carries it

    // ---- 8. the use is real: the bill of both answers is taken from the package (a stream is billed when it ends), and the panel shows it ----
    await expect
      .poll(async () => (await getBooking(client, bookingId)).unitsUsed, { message: "both answers are billed to the package", timeout: 30_000, intervals: [500, 1_000, 2_000] })
      .toBe(2 * BILL);
    const detail = new BookingDetailPage(page);
    await detail.goto(bookingId);
    await expect(detail.usage()).toHaveText(`wykorzystano ${Math.floor((2 * BILL) / 1000)} z 500 tys. tokenów`);
    await expect(detail.value("Aktywny")).toBeVisible();
    await expect(detail.cancelButton()).toHaveCount(0); // used: no cancel with a full refund any more
    await expectNoSeriousA11yViolations(page);
    await keys.goto();
    await expect(keys.rows().filter({ hasText: "Moje IDE (j8)" })).not.toContainText("jeszcze nie użyto"); // the key shows when it was last used

    // ---- 9. revoking the key in the UI ends its use at once ----
    await page.getByRole("button", { name: "Unieważnij: Moje IDE (j8)" }).click();
    await expect(page.getByRole("alertdialog", { name: "Unieważnić klucz?" })).toBeVisible();
    await page.getByRole("button", { name: "Tak, unieważnij" }).click();
    await expect(page.getByText("Klucz został unieważniony.")).toBeVisible();
    await expectOpenAiError(await models(key), 401, "invalid_api_key");
    await expectOpenAiError(await chat(key, SLUG, "Pytanie po unieważnieniu klucza."), 401, "invalid_api_key");
  });
});
