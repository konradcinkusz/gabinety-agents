import { urls } from "../../harness/env";
import { fetchRetrying, json } from "../../harness/http";

/** The Directory's public reads (CONTRACTS 3.2), the "state that follows" of J1 and J2. Status and shapes only; nothing is logged. */
export type PublicOffering = { id: string; title: string; description: string; durationMinutes: number; price: { amountMinor: number }; kind: "tasks" | "chat"; units: number };
export type PublicProfile = { slug: string; displayName: string; headline: string; bio: string; offerings: PublicOffering[] };

/** The Polish word after a number of tasks, as the profile writes it (1 zadanie, 2 to 4 zadania, otherwise zadań; 12 to 14 are zadań). */
export function taskWord(n: number): string {
  if (n === 1) return "zadanie";
  return n % 10 >= 2 && n % 10 <= 4 && !(n % 100 >= 12 && n % 100 <= 14) ? "zadania" : "zadań";
}

/** What a package card says about its content, written out here as the oracle: "5 zadań, typowy czas ok. 30 min" or "500 tys. tokenów, API zgodne z OpenAI" (units of Chat are thousands of tokens). */
export const factsOf = (o: PublicOffering): string =>
  o.kind === "chat" ? `${o.units} tys. tokenów, API zgodne z OpenAI` : `${o.units} ${taskWord(o.units)}, typowy czas ok. ${o.durationMinutes} min`;
export type Category = { slug: string; namePl: string };

export async function taxonomy(): Promise<{ problems: Category[]; techs: Category[] }> {
  return json(await fetchRetrying(`${urls.directory}/api/v1/taxonomy`), "taxonomy");
}

/** Slugs of the listed practitioners of one problem category (first page, as the web page shows it). */
export async function listedInProblem(problemSlug: string): Promise<{ slug: string; displayName: string }[]> {
  const page = await json<{ items: { slug: string; displayName: string }[] }>(
    await fetchRetrying(`${urls.directory}/api/v1/practitioners?problem=${encodeURIComponent(problemSlug)}`),
    "list by problem",
  );
  return page.items;
}

/** The public profile, or `null` for the 404 the Directory gives every slug that is not Approved. */
export async function publicProfile(slug: string): Promise<PublicProfile | null> {
  const res = await fetchRetrying(`${urls.directory}/api/v1/practitioners/${encodeURIComponent(slug)}`);
  if (res.status === 404) return null;
  // On the wire the profile is `{ card: { slug, displayName, headline, ... }, bio, offerings, ... }` (the record `PractitionerProfile(Card, ...)`);
  // the flat shape CONTRACTS 3.2 may suggest is read too, so this helper reports the Directory's data either way.
  const raw = await json<{ card?: Partial<PublicProfile> } & Partial<PublicProfile>>(res, "public profile");
  const c = raw.card ?? raw;
  return { slug: c.slug ?? "", displayName: c.displayName ?? "", headline: c.headline ?? "", bio: raw.bio ?? "", offerings: raw.offerings ?? [] };
}
