import { expect } from "@playwright/test";
import { urls } from "./env";

/**
 * The public agent API (`/v1`, PLAN 2.3) as a tool outside the browser would call it, with a key (`Authorization: Bearer gab_...`). The service is Booking, but the customer's one
 * address is the site's (ADR-0013 decision 1: the web host forwards `/v1` to Booking), so the journeys call `urls.web`. `directModels` asks Booking itself, to compare.
 * Nothing here logs a key or a body: messages and answers are user text. The calls are not retried on 429: a journey that sends a few requests stays far below the limit.
 */
const call = (path: string, key: string | null, init: RequestInit = {}, base: string = urls.web) =>
  fetch(`${base}${path}`, { ...init, headers: { ...(key ? { Authorization: `Bearer ${key}` } : {}), "Content-Type": "application/json", ...(init.headers ?? {}) } });

export interface OpenAiError { error: { message: string; type: string; code: string } }
export interface Completion {
  object: string;
  model: string;
  choices: { message: { role: string; content: string }; finish_reason: string }[];
  usage: { prompt_tokens: number; completion_tokens: number; total_tokens: number };
}

export const models = (key: string | null) => call("/v1/models", key);

/** The same call made to the Booking service itself (not through the site). */
export const directModels = (key: string | null) => call("/v1/models", key, {}, urls.booking);

export const chat = (key: string | null, model: string, content: string, extra: Record<string, unknown> = {}) =>
  call("/v1/chat/completions", key, { method: "POST", body: JSON.stringify({ model, messages: [{ role: "user", content }], ...extra }) });

/** The data lines of a server-sent-events body, parsed: the JSON chunks and whether `[DONE]` ended it. */
export function parseSse(body: string): { chunks: Record<string, unknown>[]; done: boolean; doneIsLast: boolean } {
  const data = body.split("\n").filter((l) => l.startsWith("data: ")).map((l) => l.slice("data: ".length));
  const done = data.includes("[DONE]");
  return { chunks: data.filter((d) => d !== "[DONE]").map((d) => JSON.parse(d) as Record<string, unknown>), done, doneIsLast: data.at(-1) === "[DONE]" };
}

/** Asserts the answer is the OpenAI-shaped error with the given status and code (the service never answers `problem+json` on `/v1`). */
export async function expectOpenAiError(res: Response, status: number, code: string): Promise<void> {
  expect(res.status).toBe(status);
  expect(res.headers.get("content-type") ?? "").toContain("application/json");
  const body = (await res.json()) as OpenAiError;
  expect(body.error.code).toBe(code);
  expect(typeof body.error.message).toBe("string");
}
