import { expect, type Locator, type Page } from "@playwright/test";
import { awaitCircuitIfInteractive, gotoInteractive, watchCircuit } from "./circuit";
import { bookingIdFromUrl } from "./booking-api";

/** Page objects of the booking and review flow (J3, J4). Role and label locators; texts are the Polish strings of web/src/components/{booking,bookings,reviews,profile}/strings.ts. */
export class PractitionerProfilePage {
  constructor(readonly page: Page) {}
  /**
   * The profile page is rendered by the web app from the Directory, which limits each client address to 60 requests a minute (shared with the whole run),
   * so the page can be an error page for a moment. Reload until the h1 is there (bounded); on failure say what the page showed (title and first heading only).
   */
  async goto(slug: string, displayName: string) {
    const h1 = this.page.getByRole("heading", { level: 1, name: displayName });
    await gotoInteractive(this.page, `/gabinet/${slug}`);
    try {
      await expect(async () => {
        if (!(await h1.isVisible())) await this.page.reload();
        await expect(h1).toBeVisible({ timeout: 5_000 });
      }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    } catch (err) {
      const first = await this.page.getByRole("heading").first().textContent({ timeout: 2_000 }).catch(() => null);
      throw new Error(`profile page of ${slug} did not render: title "${await this.page.title()}", first heading "${first ?? ""}"`, { cause: err });
    }
  }
  /** The offering's card: the one list item that holds the offering's heading (the time list inside it has no heading). */
  offering(title: string): Locator {
    return this.page.getByRole("listitem").filter({ has: this.page.getByRole("heading", { level: 3, name: title }) });
  }
  /** C-01: a package has no time slot; "Wykup dostęp" of the offering's card leads straight to the purchase form. */
  async purchaseLink(offeringTitle: string): Promise<Locator> {
    const link = this.offering(offeringTitle).getByRole("link", { name: `Wykup dostęp: ${offeringTitle}` });
    await expect(link).toBeVisible();
    return link;
  }
  /** What the card says about the package: "5 zadań, typowy czas ok. 30 min" or "500 tys. tokenów, API zgodne z OpenAI", and its validity. */
  offeringFacts(title: string): Locator { return this.offering(title); }
  /** The "Agent AI" label next to the agent's name (PLAN decision 8). */
  aiBadge(): Locator { return this.page.getByText("Agent AI", { exact: true }).first(); }
  /** Clicks the purchase link and waits for the booking form. If the host renders it interactively, nothing typed before its circuit is live would count, so the circuit is awaited. */
  async openBookingForm(link: Locator) {
    const circuit = watchCircuit(this.page);
    const answer = this.page.waitForResponse((r) => ["document", "fetch", "xhr"].includes(r.request().resourceType()) && /\/rezerwacja\/[0-9a-f-]{36}/.test(new URL(r.url()).pathname));
    await link.click();
    const res = await answer;
    await this.page.waitForURL(/\/rezerwacja\//);
    await awaitCircuitIfInteractive(res, circuit); // enhanced navigation answers a fetch, not a document; a static form has no circuit, an interactive one is awaited
  }
  newBadge(): Locator { return this.page.getByText("Nowy", { exact: true }); }
  reviewsSection(): Locator { return this.page.getByRole("region", { name: "Opinie" }); }
}

export class BookingFormPage {
  constructor(readonly page: Page) {}
  brief(): Locator { return this.page.getByLabel("Do czego chcesz użyć agenta?"); }
  askAssistant(): Locator { return this.page.getByRole("button", { name: "Pomóż mi opisać zastosowanie" }); }
  pay(): Locator { return this.page.getByRole("button", { name: "Zapłać" }); }
  assistantUnavailableNote(): Locator { return this.page.getByText("Asystent jest chwilowo niedostępny. Opisz zastosowanie samodzielnie i wykup dostęp."); }
  /** Everything the assistant shows when it does have a suggestion. */
  suggestionParts(): Locator[] {
    return [
      this.page.getByLabel("Propozycja opisu"),
      this.page.getByText("Sugerowana kategoria"),
      this.page.getByRole("button", { name: "Dodaj propozycję do opisu" }),
    ];
  }
  /** Fills the brief, pays and lands on the mock checkout; returns the booking id from its address. */
  async submit(text: string): Promise<string> {
    await this.brief().fill(text);
    await this.pay().click();
    await this.page.waitForURL(/\/checkout\/mock\//);
    return bookingIdFromUrl(this.page.url());
  }
}

export class MockCheckoutPage {
  constructor(readonly page: Page) {}
  succeed(): Locator { return this.page.getByRole("button", { name: "Symuluj udaną płatność" }); }
  /**
   * The checkout and confirmation pages read the booking through the BFF; when the per-address rate limit (60/min, shared with the whole run) answers 429
   * they show "Odśwież stronę" and no button. A reload is what that message asks for, so reload until the page has the booking.
   */
  async paySuccessfully() {
    await expect(async () => {
      if (!(await this.succeed().isVisible())) await this.page.reload();
      await expect(this.succeed()).toBeEnabled({ timeout: 5_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    await this.succeed().click();
    await this.page.waitForURL(/\/rezerwacja\/potwierdzenie\//);
    await expect(async () => {
      if (!(await this.page.getByText("Dostęp aktywny. Dziękujemy!").isVisible())) await this.page.reload();
      await expect(this.page.getByText("Dostęp aktywny. Dziękujemy!")).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
  }
}

/** The confirmation page of an active package (/rezerwacja/potwierdzenie/{id}). */
export class ConfirmationPage {
  constructor(readonly page: Page) {}
  heading(): Locator { return this.page.getByRole("heading", { level: 1, name: "Dostęp aktywny" }); }
  createKeyLink(): Locator { return this.page.getByRole("link", { name: "Utwórz klucz API" }); }
  firstTaskLink(): Locator { return this.page.getByRole("link", { name: "Zleć pierwsze zadanie" }); }
  connectModel(): Locator { return this.page.getByTestId("connect-model"); } // no accessible name: a bare <code> in a definition list
  connectBaseUrl(): Locator { return this.page.getByTestId("connect-base-url"); }
}

export class ReviewFormPage {
  constructor(readonly page: Page) {}
  async goto(bookingId: string) {
    await gotoInteractive(this.page, `/moje/opinie/${bookingId}`);
    await expect(this.page.getByRole("button", { name: "Wyślij opinię" })).toBeVisible();
  }
  async submit(rating: number, text: string) {
    await this.page.getByRole("radio", { name: `${rating} z 5` }).check();
    await this.page.getByLabel("Treść opinii").fill(text);
    await this.page.getByRole("button", { name: "Wyślij opinię" }).click();
  }
}
