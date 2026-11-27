import { randomBytes } from "node:crypto";
import { signInViaBff } from "../harness/auth";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { demoPassword, urls } from "../harness/env";
import { expect, test } from "../harness/fixtures";
import { publicProfile } from "./support/directory-api";
import { PractitionerArea, RegisterPage, VerificationQueuePage } from "./support/pages";

// J9 (E-04): one provider account runs two agents. Each has its own profile, package and approval: the administrator approves one, the other stays in the queue.
// Everything is in the `demo-` namespace under a unique suffix, like J2, so the journey neither depends on nor disturbs the seeded data.

const NIP_WEIGHTS = [6, 5, 7, 2, 3, 4, 5, 6, 7];
/** A checksum-valid NIP in the synthetic range 99xxxxxxxx (the Directory checks the checksum only). */
function syntheticNip(seed: number): string {
  for (let n = 990_000_000 + (seed % 9_000_000); ; n++) {
    const d = n.toString().padStart(9, "0");
    const check = [...d].reduce((sum, c, i) => sum + Number(c) * NIP_WEIGHTS[i], 0) % 11;
    if (check !== 10) return d + check;
  }
}

const BIO =
  "Jestem agentem AI, który przegląda kod i testy dla zespołów: wskazuję kruche miejsca zamiast tylko zielenić potok. " +
  "Z Twoich materiałów wyciągam najważniejsze ryzyka i ustalam kolejność poprawek. " +
  "Odpowiadam spokojnie, bez żargonu, a na końcu dostajesz krótką listę kroków do wdrożenia.";

test.describe("J9 one provider, two agents, approved one at a time", () => {
  test("two agents of one account have separate profiles, packages and approvals", async ({ page, browser }) => {
    test.setTimeout(420_000); // two onboardings against the Directory (60 requests a minute per address)

    const uid = randomBytes(4).toString("hex");
    const email = `demo-j9-${uid}@demo.example`;
    const nameA = `Demo Pierwszy ${uid}`;
    const nameB = `Demo Drugi ${uid}`;
    const slugA = `demo-pierwszy-${uid}`;
    const slugB = `demo-drugi-${uid}`;
    const githubLogin = `demo-j9-${uid}`; // one GitHub profile, shared by the two agents of this account
    const packageA = `Przegląd kodu pierwszego ${uid}`;
    const packageB = `Przegląd kodu drugiego ${uid}`;
    const nip = syntheticNip(parseInt(uid, 16));

    async function onboard(area: PractitionerArea, headline: string, packageTitle: string) {
      await area.fillProfile({ headline, bio: BIO, portfolioUrl: `https://${uid}.portfolio.example.com/`, nip });
      await area.addOffering({ title: packageTitle, description: "Oceniam Twój kod i podpowiadam, co poprawić najpierw.", price: "150", kind: "Tasks", units: "3" });
      await area.linkGithub(githubLogin);
      await area.submitForVerification();
      await expect(page.getByText("Czeka na weryfikację", { exact: true })).toBeVisible();
    }

    await test.step("the provider registers, creates the first agent and sends it for verification", async () => {
      await new RegisterPage(page).registerAs(email, demoPassword);
      const first = new PractitionerArea(page);
      await first.createProfile(nameA);
      await onboard(first, "Agent AI do przeglądu kodu, pierwszy", packageA);
    });

    await test.step("the same account adds a second agent: its own panel, a draft, the first agent is untouched", async () => {
      const first = new PractitionerArea(page);
      await first.addAgent(nameB);
      await expect(page).toHaveURL(new RegExp(`agent=${slugB}`));
      await expect(page.getByTestId("agent-bar")).toContainText(slugB);
      await expect(page.getByText("Szkic", { exact: true })).toBeVisible(); // a new agent starts as a draft, whatever the first one is
      await expectNoSeriousA11yViolations(page);
    });

    await test.step("the second agent gets its own profile, its own package and the same GitHub profile, and is sent for verification", async () => {
      await onboard(new PractitionerArea(page, slugB), "Agent AI do przeglądu kodu, drugi", packageB);
      // the packages are per agent: B's list has only B's package
      await page.goto(`/panel-agenta/oferty?agent=${slugB}`);
      await expect(page.getByRole("list", { name: "Twoje pakiety" }).getByRole("listitem").filter({ hasText: packageB })).toHaveCount(1);
      await expect(page.getByText(packageA)).toHaveCount(0);
      await page.goto(`/panel-agenta/oferty?agent=${slugA}`);
      await expect(page.getByRole("list", { name: "Twoje pakiety" }).getByRole("listitem").filter({ hasText: packageA })).toHaveCount(1);
      await expect(page.getByText(packageB)).toHaveCount(0);
    });

    // The administrator works in a context of their own.
    const adminContext = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
    try {
      await signInViaBff(adminContext, "demo-admin");
      const admin = await adminContext.newPage();
      const queue = new VerificationQueuePage(admin);

      await test.step("the administrator sees both in the queue and approves only the first", async () => {
        await queue.goto();
        await expect(queue.row(nameA)).toBeVisible();
        await expect(queue.row(nameB)).toBeVisible();
        await queue.approve(nameA);
        await expect(queue.row(nameA)).toHaveCount(0);
        await expect(queue.row(nameB), "approving one agent does not approve the other").toBeVisible();
      });
    } finally {
      await adminContext.close();
    }

    await test.step("the overview lists both with their own status, and only the approved one is public", async () => {
      await page.goto("/panel-agenta");
      await expect(page.getByRole("heading", { level: 1, name: "Twoi agenci" })).toBeVisible();
      const rows = page.getByRole("list", { name: "Lista agentów" }).getByRole("listitem");
      await expect(rows).toHaveCount(2);
      await expect(rows.filter({ hasText: nameA })).toContainText("Zatwierdzony");
      await expect(rows.filter({ hasText: nameB })).toContainText("Czeka na weryfikację");
      await expectNoSeriousA11yViolations(page);

      expect((await publicProfile(slugA))?.displayName).toBe(nameA);
      expect(await publicProfile(slugB), "the second agent is not approved, so it is a 404 in the public API").toBeNull();

      await rows.filter({ hasText: nameB }).getByRole("link", { name: `Otwórz panel: ${nameB}` }).click();
      await expect(page.getByTestId("agent-bar")).toContainText(slugB);
      await expect(page.getByText("Czeka na weryfikację", { exact: true })).toBeVisible();
    });
  });
});
