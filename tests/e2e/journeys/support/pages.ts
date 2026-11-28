import { expect, type Locator, type Page } from "@playwright/test";
import { gotoInteractive } from "../../harness/circuit";

/** Page objects of J1 and J2 (README: role and label first). Polish strings are the app's own. */

export class CategoryNav {
  constructor(readonly page: Page) {}
  /** The problem filter links ("Filtry"); a category link leads to /agenci/<slug>. */
  link(name: string): Locator { return this.page.getByRole("navigation", { name: "Filtry" }).getByRole("link", { name, exact: true }); }
}

export class PublicProfilePage {
  constructor(readonly page: Page) {}
  heading(name: string): Locator { return this.page.getByRole("heading", { level: 1, name }); }
  bookLink(offeringTitle: string): Locator { return this.page.getByRole("link", { name: `Wykup dostęp: ${offeringTitle}` }); }
  /** The "Agent AI" label next to the agent's name (PLAN decision 8). */
  aiBadge(): Locator { return this.page.getByText("Agent AI", { exact: true }).first(); }
  /** One package card: the list item that holds the package's h3. */
  offering(title: string): Locator { return this.page.getByRole("listitem").filter({ has: this.page.getByRole("heading", { level: 3, name: title }) }); }
  connectSection(): Locator { return this.page.getByRole("heading", { level: 2, name: "Jak się połączyć" }); }
}

export class RegisterPage {
  constructor(readonly page: Page) {}
  async registerAs(email: string, password: string) {
    await gotoInteractive(this.page, "/rejestracja");
    await this.page.getByLabel("Adres e-mail").fill(email);
    await this.page.getByLabel("Hasło").fill(password);
    await this.page.getByRole("checkbox", { name: /Akceptuję Regulamin/ }).check();
    await this.page.getByRole("button", { name: "Załóż konto" }).click();
    await expect(this.page).not.toHaveURL(/\/rejestracja/);
  }
}

/** /panel-agenta and its tabs: the practitioner's side of onboarding. */
export class PractitionerArea {
  /** `agent` is the slug of the agent the panel works on (E-04); leave it out for an account with one agent. */
  constructor(readonly page: Page, readonly agent?: string) {}
  /** The address of a panel page for the chosen agent: `?agent=<slug>` is added after any query the page already has. */
  private at(path: string): string {
    if (!this.agent) return path;
    return `${path}${path.includes("?") ? "&" : "?"}agent=${encodeURIComponent(this.agent)}`;
  }
  /** E-04: one more agent for the signed-in provider account, from /panel-agenta/nowy; lands in the new agent's panel. */
  async addAgent(displayName: string) {
    await gotoInteractive(this.page, "/panel-agenta/nowy");
    await this.page.getByLabel("Nazwa wyświetlana").fill(displayName);
    await this.page.getByRole("button", { name: "Dodaj agenta" }).click();
    await expect(this.page.getByRole("heading", { level: 1, name: "Panel dostawcy" })).toBeVisible();
  }
  async createProfile(displayName: string) {
    await gotoInteractive(this.page, "/panel-agenta");
    await this.page.getByLabel("Nazwa wyświetlana").fill(displayName);
    await this.page.getByRole("button", { name: "Załóż gabinet" }).click();
    await expect(this.page.getByRole("heading", { level: 1, name: "Panel dostawcy" })).toBeVisible();
  }
  checklistItem(name: string): Locator { return this.page.getByRole("listitem").filter({ hasText: name }); }
  /** One package in the provider's list (/panel-agenta/oferty): the list item that holds its title. */
  offer(title: string): Locator { return this.page.getByRole("list", { name: "Twoje pakiety" }).getByRole("listitem").filter({ hasText: title }); }

  async fillProfile(p: { headline: string; bio: string; portfolioUrl: string; nip: string }) {
    await gotoInteractive(this.page, this.at("/panel-agenta/profil"));
    await this.page.getByLabel("Nagłówek").fill(p.headline);
    await this.page.getByLabel("Opis").fill(p.bio);
    await this.page.getByLabel("Link do portfolio").fill(p.portfolioUrl);
    await this.page.getByLabel("NIP").fill(p.nip);
    await this.page.getByRole("button", { name: "Zapisz" }).click();
    await expect(this.page.getByText("Zapisano.")).toBeVisible();
  }

  async addOffering(o: { title: string; description: string; price: string; kind?: "Tasks" | "Chat"; units?: string }) {
    await gotoInteractive(this.page, this.at("/panel-agenta/oferty?nowa=1")); // the form is closed until "Dodaj pakiet" (UX-042)
    await this.page.getByLabel("Nazwa pakietu").fill(o.title);
    await this.page.getByLabel("Opis pakietu").fill(o.description);
    await this.page.getByLabel("Cena (zł)").fill(o.price);
    await this.page.getByLabel("Rodzaj pakietu").selectOption(o.kind ?? "Tasks"); // "Zadania" or "Czat API"
    await this.page.getByLabel("Liczba jednostek").fill(o.units ?? "1");
    await this.page.getByLabel("Rodzaj problemu").selectOption({ index: 1 }); // index 0 is "Wybierz…"
    await this.page.getByRole("group", { name: /Technologie/ }).getByRole("checkbox").first().check();
    await this.page.getByRole("button", { name: "Zapisz" }).click();
    await expect(this.page.getByText("Zapisano.")).toBeVisible();
    await expect(this.page.getByText(o.title)).toBeVisible();
  }

  async linkGithub(login: string) {
    await gotoInteractive(this.page, this.at("/panel-agenta"));
    await this.page.getByLabel("Login GitHub").fill(login);
    await this.page.getByRole("button", { name: "Pokaż kod" }).click();
    await this.page.getByRole("button", { name: "Sprawdź kod" }).click();
    await expect(this.page.getByText(`Połączono konto GitHub: ${login}.`)).toBeVisible();
  }

  async submitForVerification() {
    await this.page.getByRole("button", { name: "Wyślij do weryfikacji" }).click();
    await expect(this.page.getByText("Profil wysłany. Administrator sprawdzi go i da znać.")).toBeVisible();
  }
}

/** /admin/weryfikacja: the queue and its row actions. */
export class VerificationQueuePage {
  constructor(readonly page: Page) {}
  async goto(status?: "Submitted" | "Approved") {
    await gotoInteractive(this.page, status ? `/admin/weryfikacja?status=${status}` : "/admin/weryfikacja");
    await expect(this.page.getByRole("heading", { level: 1, name: "Weryfikacja agentów" })).toBeVisible();
  }
  row(displayName: string): Locator {
    return this.page.getByRole("listitem").filter({ has: this.page.getByRole("link", { name: displayName, exact: true }) });
  }
  async approve(displayName: string) {
    await this.row(displayName).getByRole("button", { name: "Zatwierdź" }).click();
  }
}
