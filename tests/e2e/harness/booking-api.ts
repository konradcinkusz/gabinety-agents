import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { resetAndReseed } from "./demo-data";
import { accessTokenOf } from "./auth";
import { urls } from "./env";
import { fetchRetrying, json } from "./http";

/**
 * Helpers of the booking and review journeys (J3, J4). Additive: nothing here changes the harness.
 * Calls on behalf of a signed-in user go straight to the service with that user's bearer token (the host has no proxy).
 * A helper never logs a body: briefs and review texts are user free text.
 */
type Opts = Parameters<APIRequestContext["get"]>[1];
/** `request.get/post` of one service's `/api/v1`, straight to the service with the context's bearer token (the host has no proxy, see auth.ts). */
function svc(request: APIRequestContext, service: "booking" | "reputation") {
  const url = (path: string) => `${urls[service]}/api/v1${path}`;
  const withAuth = (o: Opts): Opts => {
    const token = accessTokenOf(request);
    return { ...o, headers: { ...(o?.headers ?? {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) } };
  };
  return {
    get: (path: string, o?: Opts) => request.get(url(path), withAuth(o)),
    post: (path: string, o?: Opts) => request.post(url(path), withAuth(o)),
  };
}
const booking = (r: APIRequestContext) => svc(r, "booking");
const reputation = (r: APIRequestContext) => svc(r, "reputation");

/**
 * Every service limits each client address to 60 requests a minute and everything here is `localhost`, so a 429 (or a 503 while a service restarts)
 * is normal. Same policy as `fetchRetrying` (honour `Retry-After`, grow the delay, bounded budget) for calls made through a browser context.
 */
export async function retrying(call: () => Promise<APIResponse>, budgetMs = 120_000): Promise<APIResponse> {
  const deadline = Date.now() + budgetMs;
  let delay = 1_000;
  for (;;) {
    const res = await call();
    if (res.status() !== 429 && res.status() !== 503) return res;
    const retryAfter = Number(res.headers()["retry-after"]);
    const wait = Number.isFinite(retryAfter) && retryAfter > 0 ? Math.min(retryAfter * 1_000, 65_000) : delay;
    if (Date.now() + wait > deadline) return res; // out of budget: the caller fails with the real status
    await new Promise((r) => setTimeout(r, wait));
    delay = Math.min(delay * 2, 10_000);
  }
}

export type Money = { amountMinor: number; currency: string };
export interface PublicOffering { id: string; title: string; durationMinutes: number; price: Money; kind: "tasks" | "chat"; units: number } // the public profile writes the kind in lower case (PublicJson); Booking's views write "Tasks" / "Chat"
export interface PublicProfile {
  id: string;
  slug: string;
  displayName: string;
  rating?: { average: number; count: number } | null;
  isNew: boolean;
  offerings: PublicOffering[];
}
export interface BookingState {
  id: string;
  status: string;
  startUtc: string;
  endUtc: string;
  offeringTitle: string;
  price: Money;
  brief?: { text: string; problemSlug?: string | null; aiAssisted: boolean } | null;
  payment: { status: string; refunded: Money };
  canReview: boolean;
  canCancel: boolean;
  canDispute: boolean;
  kind: "Tasks" | "Chat";
  unitsTotal: number;
  unitsUsed: number;
}
/** A task as the client reads it (`GET /api/v1/me/bookings/{id}/tasks`): `status` is lowercase, `output` is null until it succeeded. */
export interface TaskState { id: string; status: "queued" | "running" | "succeeded" | "failed"; output: string | null }

/** The Directory's public profile, read directly (fresh: the web page of the same data is cached for 5 minutes). */
export async function publicProfile(slug: string): Promise<PublicProfile> {
  return json<PublicProfile>(await fetchRetrying(`${urls.directory}/api/v1/practitioners/${encodeURIComponent(slug)}`), `profile ${slug}`); // flat per CONTRACTS 3.2 (#100)
}

/** Buys a package: no date, a package runs for 30 days from the purchase (PLAN A-02). The booking is `PendingPayment` until the simulated or mock payment. */
export async function createBooking(request: APIRequestContext, offeringId: string, briefText: string): Promise<{ bookingId: string }> {
  const key = randomUUID(); // one key for every retry of this request: a replayed 429 never books twice
  const res = await retrying(() => booking(request).post(`/bookings`, {
    headers: { "Idempotency-Key": key },
    data: { offeringId, brief: { text: briefText, aiAssisted: false } },
  }));
  expect(res.ok(), `create booking (HTTP ${res.status()})`).toBe(true);
  return (await res.json()) as { bookingId: string };
}

export async function simulatePayment(request: APIRequestContext, bookingId: string, outcome: "succeeded" | "failed"): Promise<void> {
  const res = await retrying(() => booking(request).post(`/payments/simulate`, { data: { bookingId, outcome } }));
  expect(res.ok(), `simulate payment ${outcome} (HTTP ${res.status()})`).toBe(true);
}

export async function getBooking(request: APIRequestContext, bookingId: string): Promise<BookingState> {
  const res = await retrying(() => booking(request).get(`/bookings/${bookingId}`));
  expect(res.status(), "BookingView").toBe(200);
  return (await res.json()) as BookingState;
}

/** The auto-complete job runs every 60 s, so a package whose last unit is used (or whose validity ended) becomes Completed within a minute or two: wait for the state, never sleep. */
export async function waitForStatus(request: APIRequestContext, bookingId: string, status: string, timeoutMs = 240_000): Promise<void> {
  await expect
    .poll(async () => (await getBooking(request, bookingId)).status, { message: `booking reaches ${status}`, timeout: timeoutMs, intervals: [5_000, 10_000] })
    .toBe(status);
}

/** Orders one task of a `Tasks` package as the signed-in client (the route of the panel's form, B-05). It takes a unit at once; returns the task id. The text is user text: never logged. */
export async function orderTask(request: APIRequestContext, bookingId: string, input: string): Promise<string> {
  const res = await retrying(() => booking(request).post(`/bookings/${bookingId}/tasks`, { data: { input } }));
  expect(res.status(), "order task").toBe(202);
  return ((await res.json()) as { id: string }).id;
}

export async function listTasks(request: APIRequestContext, bookingId: string): Promise<TaskState[]> {
  const res = await retrying(() => booking(request).get(`/me/bookings/${bookingId}/tasks`));
  expect(res.status(), "list tasks").toBe(200);
  return (await res.json()) as TaskState[];
}

/** The task job runs every 5 s: wait until no task of the package is queued or running (never a sleep), and return them. */
export async function waitForTasksDone(request: APIRequestContext, bookingId: string, timeoutMs = 60_000): Promise<TaskState[]> {
  let last: TaskState[] = [];
  await expect
    .poll(async () => { last = await listTasks(request, bookingId); return last.filter((t) => t.status === "queued" || t.status === "running").length; },
      { message: "the agent answers the tasks", timeout: timeoutMs, intervals: [1_000, 2_000, 5_000] })
    .toBe(0);
  return last;
}

export async function createReview(request: APIRequestContext, bookingId: string, rating: number, text: string): Promise<{ status: string }> {
  const res = await retrying(() => reputation(request).post(`/reviews`, { data: { bookingId, rating, text } }));
  expect(res.ok(), `create review (HTTP ${res.status()})`).toBe(true);
  return (await res.json()) as { status: string };
}

/** The status code of a review attempt, for the one that must be refused (a second review of the same booking: 409 `already_reviewed`). */
export async function reviewAttemptStatus(request: APIRequestContext, bookingId: string, rating: number, text: string): Promise<number> {
  return (await retrying(() => reputation(request).post(`/reviews`, { data: { bookingId, rating, text } }))).status();
}

/** The booking id out of a `/checkout/mock/<id>` or `/dostep/potwierdzenie/<id>` address. */
export function bookingIdFromUrl(url: string): string {
  const m = /\/(?:checkout\/mock|dostep\/potwierdzenie|moje\/opinie|moje\/dostepy)\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/i.exec(url);
  if (!m) throw new Error("no booking id in the address");
  return m[1]!;
}

/**
 * `afterAll` cleanup of a journey (SPEC 7). CI runs on a fresh ephemeral stack, so the reseed (1 to 6 minutes) is off by default; set `E2E_RESEED=1` for a local
 * re-run on a stack that is kept (journeys assume the seeded starting data, e.g. J3 needs a profile with no review). Not for test-scoped fixtures: use inside `afterAll`.
 */
export async function reseedIfRequested(): Promise<void> {
  if (process.env.E2E_RESEED === "1") await resetAndReseed();
}
