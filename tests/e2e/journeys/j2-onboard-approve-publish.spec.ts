import { randomBytes } from "node:crypto";
import { signInViaBff } from "../harness/auth";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { demoPassword, urls } from "../harness/env";
import { expect, test } from "../harness/fixtures";
import { PractitionerListPage } from "../harness/pages";
import { publicProfile } from "./support/directory-api";
import { PractitionerArea, PublicProfilePage, RegisterPage, VerificationQueuePage } from "./support/pages";

// J2 (SPEC 10, D-02): a new provider creates an agent, adds a task package and a Chat package, an administrator approves, the agent is visible with both packages.
// Everything this journey creates is in the `demo-` namespace under a unique suffix (the account, the profile slug), so it neither depends on nor
// disturbs what other journeys create. With E2E_RESEED=1 the Directory reset in afterAll removes it (and, as SPEC 7 defines, reseeds the dataset); by default it is left, as CI's stack is thrown away.

const NIP_WEIGHTS = [6, 5, 7, 2, 3, 4, 5, 6, 7];
/** A checksum-valid NIP in the synthetic range 99xxxxxxxx (the Directory checks the checksum only), derived from the run's suffix. */
function syntheticNip(seed: number): string {
  for (let n = 990_000_000 + (seed % 9_000_000); ; n++) {
    const d = n.toString().padStart(9, "0");
    const check = [...d].reduce((sum, c, i) => sum + Number(c) * NIP_WEIGHTS[i], 0) % 11;
    if (check !== 10) return d + check;
  }
}

const BIO =
  "Jestem agentem AI od testów end to end dla zespołów frontendowych: wykrywam błędy zamiast tylko zielenić potok. " +
  "Z Twoich scenariuszy wskażę kruche selektory i niestabilne oczekiwania oraz ustalę kolejność poprawek. " +
  "Odpowiadam spokojnie, bez żargonu, a na końcu dostajesz krótką listę kroków do wdrożenia.";

test.describe("J2 provider creates an agent, administrator approves, profile becomes public", () => {
  // CI runs on a fresh ephemeral stack, so the (slow, 1 to 6 minutes) reset runs only when asked: E2E_RESEED=1 for a local re-run on a long-lived profile.
  // The journey does not need it: it creates its own `demo-` practitioner under a unique suffix and reads nothing another run left behind.
  test.afterAll(async ({ demoData }) => {
    if (process.env.E2E_RESEED !== "1") return;
    test.setTimeout(600_000); // the default hook timeout (60 s) would kill the seeder halfway and leave a half-reset Directory
    await demoData.resetAndReseed();
  });

  test("onboarding to approval to a public profile", async ({ page, browser }) => {
    test.setTimeout(240_000); // many page loads against the Directory (60 requests a minute per address: the harness retries only direct calls)

    const uid = randomBytes(4).toString("hex");
    const email = `demo-j2-${uid}@demo.example`;
    const displayName = `Demo Jedyny ${uid}`; // the Directory derives the slug from the name; "demo-" keeps it in the reset's namespace
    const slug = `demo-jedyny-${uid}`;
    const headline = "Agent AI do testów end to end dla zespołów frontendowych";
    const offeringTitle = `Przegląd testów end to end ${uid}`;
    const chatTitle = `Czat o testach end to end ${uid}`;
    const githubLogin = `demo-j2-${uid}`;
    const area = new PractitionerArea(page);

    await test.step("a visitor registers and creates an agent's profile: a draft", async () => {
      await new RegisterPage(page).registerAs(email, demoPassword);
      await area.createProfile(displayName);
      await expect(page.getByText("Szkic", { exact: true })).toBeVisible();
      await expect(area.checklistItem("Nagłówek profilu")).toContainText("Do zrobienia");
      await expectNoSeriousA11yViolations(page);
    });

    await test.step("the provider completes the profile, a task package, a Chat package and the GitHub link", async () => {
      await area.fillProfile({ headline, bio: BIO, portfolioUrl: `https://${slug}.portfolio.example.com/`, nip: syntheticNip(parseInt(uid, 16)) });
      await expectNoSeriousA11yViolations(page);
      await area.addOffering({ title: offeringTitle, description: "Oceniam Twoje scenariusze testowe i stabilność oczekiwań.", price: "180", kind: "Tasks", units: "5" });
      await expectNoSeriousA11yViolations(page);
      await area.addOffering({ title: chatTitle, description: "Odpowiadam na pytania o testy w czacie zgodnym z OpenAI.", price: "99", kind: "Chat", units: "500" });
      // the list of the provider's packages says what each one is: tasks or tokens (Offers.razor, the same words as the public card)
      await expect(area.offer(offeringTitle)).toContainText("5 zadań, typowy czas ok. 30 min");
      await expect(area.offer(chatTitle)).toContainText("500 tys. tokenów, API zgodne z OpenAI");
      await expectNoSeriousA11yViolations(page);
      await area.linkGithub(githubLogin);
      for (const item of ["Nagłówek profilu", "Opis (co najmniej 200 znaków)", "Co najmniej jeden aktywny pakiet", "Połączone konto GitHub", "Poprawny numer NIP", "Link do portfolio", "Potwierdzony adres e-mail"]) {
        await expect(area.checklistItem(item)).toContainText("Gotowe");
      }
    });

    await test.step("submitting puts the profile in the queue and it is still not public", async () => {
      await area.submitForVerification();
      await expect(page.getByText("Czeka na weryfikację", { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Wyślij do weryfikacji" })).toHaveCount(0);
      expect(await publicProfile(slug), "an unapproved profile is a 404 in the public API").toBeNull();
    });

    // The administrator works in a context of their own: the practitioner stays signed in in the first one.
    const adminContext = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
    try {
      await signInViaBff(adminContext, "demo-admin");
      const admin = await adminContext.newPage();
      const queue = new VerificationQueuePage(admin);

      await test.step("the administrator finds the profile in the queue and approves it", async () => {
        await queue.goto();
        await expect(queue.row(displayName)).toBeVisible();
        await expectNoSeriousA11yViolations(admin);
        await queue.approve(displayName);
        await expect(queue.row(displayName)).toHaveCount(0); // it left the "Czeka na weryfikację" queue
      });

      await test.step("it is now under Approved, and the history records submission and approval", async () => {
        await queue.goto("Approved");
        const row = queue.row(displayName);
        await expect(row).toBeVisible();
        await expect(row.getByText("Zatwierdzony", { exact: true })).toBeVisible();
        await row.getByRole("link", { name: displayName, exact: true }).click();
        await expect(admin.getByRole("heading", { level: 1, name: displayName })).toBeVisible();
        const history = admin.getByRole("region", { name: "Historia weryfikacji" });
        await expect(history.getByRole("listitem").filter({ hasText: "Zgłoszono do weryfikacji" })).toHaveCount(1);
        await expect(history.getByRole("listitem").filter({ hasText: "Zatwierdzono" })).toHaveCount(1);
        await expectNoSeriousA11yViolations(admin);
      });
    } finally {
      await adminContext.close();
    }

    await test.step("the profile is public: in the API, on its page and in the search", async () => {
      const profile = await publicProfile(slug);
      expect(profile?.displayName).toBe(displayName);
      expect(profile?.offerings.map((o) => [o.title, o.kind, o.units]).sort()).toEqual([[chatTitle, "chat", 500], [offeringTitle, "tasks", 5]].sort());

      // The first visit to this profile page is after approval (a 404 would be cached for the page's revalidation window).
      const view = new PublicProfilePage(page);
      // The Directory limits 60 requests a minute per address and J1 ran just before: if the page render hit a 429 or a 5xx, load it again
      // (an error render is not cached; the profile itself is already proven public by the API read above).
      await expect(async () => {
        const res = await page.goto(`/agent/${slug}`);
        expect(res?.status()).toBe(200);
        await expect(view.heading(displayName)).toBeVisible({ timeout: 3_000 });
      }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
      await expect(page.getByText(headline)).toBeVisible();
      await expect(page.getByText("Zatwierdzony", { exact: true })).toBeVisible();
      await expect(view.bookLink(offeringTitle)).toBeVisible();
      await expect(view.bookLink(chatTitle)).toBeVisible();
      await expect(view.aiBadge()).toBeVisible();
      await expect(view.offering(chatTitle)).toContainText("500 tys. tokenów, API zgodne z OpenAI");
      await expect(view.offering(offeringTitle)).toContainText("5 zadań, typowy czas ok. 30 min");
      await expectNoSeriousA11yViolations(page);

      const list = new PractitionerListPage(page);
      await list.goto(`?q=${uid}`); // a search URL is rendered per request, not served from the 5-minute page cache
      await expect(list.profileLink(displayName)).toBeVisible();

      await page.goto("/panel-agenta");
      await expect(page.getByText("Zatwierdzony", { exact: true })).toBeVisible(); // the provider's own panel agrees
    });
  });
});
