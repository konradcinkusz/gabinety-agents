import { randomUUID } from "node:crypto";
import { expect, type APIRequestContext, type APIResponse, type Browser, type BrowserContext } from "@playwright/test";
import { demoPassword, emailOf, urls } from "./env";
import { accessTokenOf, registerAtIdentity, signInViaBff } from "./auth";
import { fetchRetrying, json } from "./http";

/**
 * Helpers shared by the money journeys (J5, J6, J7): set-up straight at the services with the signed-in user's token, so a journey drives only the screen it is about
 * (cancel, dispute, resolve) through the UI. Every name is in the `demo-` namespace and unique per call, so journeys (and parallel CI sessions) do not
 * share rows, and the "one fee per client and practitioner pair" rule (CONTRACTS 9.1) always starts fresh. Nothing here logs a body or a token.
 */

export const uniqueDemoName = (tag: string): string => `demo-${tag}-${randomUUID().slice(0, 8)}`;

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/**
 * The host has no `/api/proxy/<service>/...` (the browser never calls a backend), so the `/api/proxy/<service>/...` path of a call goes straight to the service with the
 * user's bearer token (`signInViaBff` keeps it). Set-up and verification only: what a journey is ABOUT still goes through the UI.
 */
function direct(ctx: APIRequestContext, path: string, headers: Record<string, string> | undefined): { url: string; headers: Record<string, string> | undefined } {
  const m = /^\/api\/proxy\/(directory|booking|reputation|assistant)\/(.*)$/.exec(path);
  if (!m) return { url: path, headers }; // a path of the web origin itself (cross-site probes, downloads) stays there
  const token = accessTokenOf(ctx);
  if (!token) throw new Error(`no access token for this context: sign in with signInViaBff first (${m[1]})`);
  return { url: `${urls[m[1] as "directory" | "booking" | "reputation" | "assistant"]}/${m[2]}`, headers: { ...headers, Authorization: `Bearer ${token}` } };
}

/** A request through the web origin that waits for a 429 or 503 (the Directory's 60 a minute per address) like `fetchRetrying` does. */
export async function bff(
  ctx: APIRequestContext,
  method: "GET" | "POST" | "PUT",
  path: string,
  { body, headers, budgetMs = 120_000 }: { body?: unknown; headers?: Record<string, string>; budgetMs?: number } = {},
): Promise<APIResponse> {
  const deadline = Date.now() + budgetMs;
  let delay = 500;
  const target = direct(ctx, path, headers);
  for (;;) {
    const res = await ctx.fetch(target.url, { method, headers: target.headers, data: body, failOnStatusCode: false });
    if ((res.status() !== 429 && res.status() !== 503) || Date.now() + delay > deadline) return res;
    const retryAfter = Number(res.headers()["retry-after"]);
    await sleep(Number.isFinite(retryAfter) && retryAfter > 0 ? Math.min(retryAfter * 1000, 65_000) : delay);
    delay = Math.min(delay * 2, 10_000);
  }
}

/** Registers a fresh demo user (DevIdentity gives a `demo-` address the `demo-` subject) and signs the context in as it. */
export async function registerDemoUser(context: BrowserContext, userName: string): Promise<void> {
  await registerAtIdentity(userName);
  await signInViaBff(context, userName);
}

/** A second signed-in actor (the practitioner, the administrator) next to the test's own `page` context. Close it when done. */
export async function actorContext(browser: Browser, userName: string): Promise<BrowserContext> {
  const ctx = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
  await signInViaBff(ctx, userName);
  return ctx;
}

export interface PublicOffering { id: string; title: string; durationMinutes: number; price: { amountMinor: number }; problemSlug: string; techSlugs: string[]; kind: "tasks" | "chat"; units: number }
export interface PublicProfile { id: string; offerings: PublicOffering[] }

export async function publicProfile(slug: string): Promise<PublicProfile> {
  return json<PublicProfile>(await fetchRetrying(`${urls.directory}/api/v1/practitioners/${slug}`), `profile ${slug}`);
}

/**
 * Adds a one-task package of the given price to a seeded approved agent (signed-in `practitionerCtx`), so a journey's money arithmetic does not depend on
 * the seeded catalogue. The title is unique (`demo-`), the text carries no contact data. Returns the offering id.
 */
export async function addOffering(practitionerCtx: APIRequestContext, like: PublicOffering, priceMinor: number, tag: string, units = 1): Promise<string> {
  const res = await bff(practitionerCtx, "POST", "/api/proxy/directory/api/v1/me/practitioner/offerings", {
    body: {
      title: `Pakiet testowy ${tag}`,
      description: "Pakiet utworzony na potrzeby testu automatycznego, wyłącznie z danymi syntetycznymi.",
      kind: "Tasks",
      units,
      durationMinutes: like.durationMinutes,
      priceMinor,
      problemSlug: like.problemSlug,
      techSlugs: like.techSlugs,
    },
  });
  // On a refusal say WHICH one (the machine code and the field names only, never the text: the offering text is ours but the habit is "no body in logs"): a 422 is validation_failed,
  // too_many_offerings or contact_details_not_allowed, and they mean different things. (CI once failed here with a bare 422 next to a J3 failure.)
  if (res.status() !== 201) {
    const b = (await res.json().catch(() => null)) as { code?: string; errors?: { field?: string }[] } | null;
    expect(res.status(), `create offering: ${b?.code ?? "-"} ${(b?.errors ?? []).map((e) => e.field).join(",")}`).toBe(201);
  }
  return ((await res.json()) as { id: string }).id;
}

export const day = (d: Date): string => d.toISOString().slice(0, 10);
export const addDays = (d: Date, n: number): Date => new Date(d.getTime() + n * 86_400_000);

export interface PlacedBooking { id: string; start: Date; end: Date }

/** A task as the client reads it (`GET /api/v1/me/bookings/{id}/tasks`). */
export interface TaskDto { id: string; status: string; output: string | null }

/** Orders one task on a package as the signed-in client (the panel's route); returns the task id. The text is user text: never logged. */
export async function orderTask(ctx: BrowserContext, bookingId: string, input: string): Promise<string> {
  const res = await bff(ctx.request, "POST", `/api/proxy/booking/api/v1/bookings/${bookingId}/tasks`, { body: { input } });
  expect(res.status(), "order task").toBe(202);
  return ((await res.json()) as { id: string }).id;
}

/** Buys a package (no date) and pays it on the mock (`payments/simulate`), so the booking is `Confirmed`. */
export async function bookAndPay(client: BrowserContext, o: { offeringId: string }): Promise<PlacedBooking> {
  const res = await bff(client.request, "POST", "/api/proxy/booking/api/v1/bookings", {
    headers: { "Idempotency-Key": randomUUID() },
    body: { offeringId: o.offeringId, brief: { text: "Opis testowy dla automatycznego scenariusza end to end, dane syntetyczne.", aiAssisted: false } },
  });
  expect(res.status(), "create booking").toBe(201);
  const { bookingId } = (await res.json()) as { bookingId: string };
  const paid = await bff(client.request, "POST", "/api/proxy/booking/api/v1/payments/simulate", { body: { bookingId, outcome: "succeeded" } });
  expect(paid.status(), "simulate payment").toBe(200);
  const view = await getBooking(client, bookingId);
  expect(view.status).toBe("Confirmed");
  return { id: bookingId, start: new Date(view.startUtc), end: new Date(view.endUtc) };
}

export interface BookingViewDto {
  id: string;
  status: string;
  startUtc: string;
  endUtc: string;
  price: { amountMinor: number };
  payment: { status: string; refunded: { amountMinor: number } };
  fee?: { amountMinor: number } | null;
  payout?: { amountMinor: number } | null;
  kind: "Tasks" | "Chat";
  unitsTotal: number;
  unitsUsed: number;
  canCancel: boolean;
  canDispute: boolean;
  canReview: boolean;
}

export async function getBooking(ctx: BrowserContext, id: string): Promise<BookingViewDto> {
  const res = await bff(ctx.request, "GET", `/api/proxy/booking/api/v1/bookings/${id}`);
  expect(res.status(), "get booking").toBe(200);
  return (await res.json()) as BookingViewDto;
}

/** CONTRACTS 9.1, written out again here on purpose: the oracle for the journeys' numbers, not a call into the product's code. */
export const FEE_BPS = 1500;
export const feeOf = (kept: number): number => Math.floor((kept * FEE_BPS + 5000) / 10000);

export interface LedgerRow { bookingId: string; kind: string; amountMinor: number }

/**
 * The ledger CSV (T-04.08b, `GET /api/v1/admin/ledger.csv`, read through the BFF proxy with the administrator's cookie) for a period, only the rows of
 * `bookingIds` (other journeys' rows and global totals are none of our business). `null` with the status when the answer is not a 200 (a caller may poll).
 */
export async function ledgerRows(admin: BrowserContext, from: string, to: string, bookingIds: string[]): Promise<{ rows: LedgerRow[] } | { status: number; code: string }> {
  const res = await bff(admin.request, "GET", `/api/proxy/booking/api/v1/admin/ledger.csv?from=${from}&to=${to}`);
  if (res.status() !== 200) {
    const code = ((await res.json().catch(() => null)) as { code?: string } | null)?.code ?? "-"; // the machine code only, never a body
    return { status: res.status(), code };
  }
  const [header, ...lines] = (await res.text()).split(/\r?\n/).filter((l) => l.length > 0);
  expect(header).toBe("booking_id,kind,amount_minor,currency,created_at");
  return {
    rows: lines
      .map((l) => l.split(","))
      .filter(([id]) => bookingIds.includes(id))
      .map(([bookingId, kind, amount]) => ({ bookingId, kind, amountMinor: Number(amount) })),
  };
}

/**
 * The download route of /admin/ksiega (BFF `/admin/ksiega/csv`, which streams Booking's ledger.csv with the administrator's cookie): one request, retried a few
 * times while it answers anything but 200 (it maps every upstream failure, 429 included, to 503). Returns what the browser would get.
 */
export async function ledgerDownload(admin: BrowserContext, from: string, to: string): Promise<{ status: number; contentType: string; disposition: string; header: string }> {
  let last = { status: 0, contentType: "", disposition: "", header: "" };
  for (let attempt = 1; attempt <= 4; attempt++) {
    const res = await admin.request.get(`/admin/ksiega/csv?from=${from}&to=${to}`, { failOnStatusCode: false });
    last = {
      status: res.status(),
      contentType: res.headers()["content-type"] ?? "",
      disposition: res.headers()["content-disposition"] ?? "",
      header: res.status() === 200 ? ((await res.text()).split(/\r?\n/)[0] ?? "") : "",
    };
    if (last.status === 200) break;
    await sleep(5_000); // a rate limit window, not business time
  }
  return last;
}
