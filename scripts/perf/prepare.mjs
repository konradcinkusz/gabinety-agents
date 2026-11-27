#!/usr/bin/env node
// Prepares the inputs of scripts/measure-performance.sh from the running, seeded system: the filtered-list URLs, the web page URLs and the POST /bookings requests (the purchase of a
// package without a term: no startUtc; signed in as the demo clients, who need a booking-creation limit of their own: see RateLimit__PerMinute in perf-stack.sh).
//   node scripts/perf/prepare.mjs OUT_DIR [BOOKINGS=120]
import { writeFileSync, mkdirSync } from "node:fs";
import { randomUUID } from "node:crypto";

const [outDir, bookingsArg] = process.argv.slice(2);
if (!outDir) { console.error("usage: prepare.mjs OUT_DIR [BOOKINGS]"); process.exit(2); }
mkdirSync(outDir, { recursive: true });
const DIR = process.env.DIRECTORY_URL ?? "http://localhost:5101", BOOK = process.env.BOOKING_URL ?? "http://localhost:5102", ID = process.env.IDENTITY_URL ?? "http://localhost:5100", WEB = process.env.WEB_URL ?? "http://localhost:3000";
const PASSWORD = "Demo-Dev-Passw0rd!"; // the fixed development password of tools/Gabinety.DevIdentity; protects nothing
const wanted = +(bookingsArg ?? 120);
const get = async (url, token) => { const r = await fetch(url, { headers: token ? { authorization: `Bearer ${token}` } : {} }); if (!r.ok) throw new Error(`${url} answered ${r.status}`); return r.json(); };
const login = async (name) => { const r = await fetch(`${ID}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email: `${name}@demo.example`, password: PASSWORD }) }); if (!r.ok) throw new Error(`login ${name}: ${r.status}`); return (await r.json()).accessToken; };

const tax = await get(`${DIR}/api/v1/taxonomy`);
const problems = tax.problems.map((p) => p.slug), techs = tax.techs.map((t) => t.slug);

// filtered list, 12 items: every combination of a problem category and a duration, plus problem and tech, rotated so no single query is measured
const lists = [];
for (const p of problems) for (const d of [30, 60]) lists.push(`${DIR}/api/v1/practitioners?problem=${p}&duration=${d}&limit=12`);
for (const p of problems.slice(0, 4)) for (const t of techs.slice(0, 4)) lists.push(`${DIR}/api/v1/practitioners?problem=${p}&tech=${t}&limit=12`);
const full = [];
for (const u of lists) { const j = await get(u); if (j.items.length === 12) full.push(u); }
if (full.length === 0) throw new Error("no filtered query returns 12 items: is the scaled dataset seeded (--factor 200)?");

// the offerings of the six Approved demo practitioners of the catalog (the scaled ones are not used for purchases). A package has no date any more (A-02): the purchase request carries no startUtc
const base = ["demo-anna-demowska", "demo-bartosz-testowy", "demo-celina-przykladowska", "demo-dariusz-fikcyjny", "demo-ewa-wzorcowa", "demo-filip-syntetyczny"];
const offerings = [];
for (const slug of base) {
  const prof = await get(`${DIR}/api/v1/practitioners/${slug}`);
  for (const o of prof.offerings) offerings.push({ slug, offeringId: o.id });
}
if (offerings.length === 0) throw new Error("no offerings of the demo agents: is the system seeded?");

// POST /api/v1/bookings: the purchase of a package without a term (no startUtc), rotating over the offerings and the demo clients, a distinct Idempotency-Key each
const clients = ["demo-client", "demo-client-2", "demo-client-3", "demo-client-4"];
const tokens = []; for (const c of clients) tokens.push(await login(c));
const requests = [];
while (requests.length < wanted) {
  const offering = offerings[requests.length % offerings.length];
  const token = tokens[requests.length % tokens.length];
  requests.push({ method: "POST", url: `${BOOK}/api/v1/bookings`,
    headers: { authorization: `Bearer ${token}`, "content-type": "application/json", "idempotency-key": randomUUID() },
    body: { offeringId: offering.offeringId, brief: { text: `[demo-perf-${requests.length}] Pomiar wydajnosci, dane syntetyczne.`, problemSlug: null, aiAssisted: false } } });
}
writeFileSync(`${outDir}/list-urls.json`, JSON.stringify(full));
writeFileSync(`${outDir}/booking-requests.json`, JSON.stringify(requests));
const slug0 = base[5], problem0 = problems[0];
writeFileSync(`${outDir}/prep.json`, JSON.stringify({ listQueries: full.length, bookingRequests: requests.length, bookingsWanted: wanted,
  web: { category: `${WEB}/agenci/${problem0}`, list: `${WEB}/agenci`, profile: `${WEB}/agent/${slug0}`, terms: `${WEB}/regulamin`, home: `${WEB}/` } }, null, 2));
console.log(`prepared: ${full.length} list queries, ${requests.length} of ${wanted} booking requests`);
