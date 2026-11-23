import { expect, type Locator, type Page } from "@playwright/test";
import { gotoInteractive } from "./circuit";

/** Page objects of the booking detail (/moje/rezerwacje/{id}), the API keys page (/moje/klucze) and the administrator's claims list (/admin/spory), for J3, J5, J6 and J8. Role and label locators only. */
export class BookingDetailPage {
  constructor(readonly page: Page) {}
  /** Opens the detail; a page that shows an error banner instead (the services answered 429/503 under the shared rate limit) is reloaded a few times. */
  async goto(bookingId: string) {
    const heading = this.page.getByRole("heading", { level: 2 }).first();
    for (let attempt = 1; ; attempt++) {
      await gotoInteractive(this.page, `/moje/rezerwacje/${bookingId}?role=client`);
      try {
        await expect(heading).toBeVisible({ timeout: 8_000 });
        return;
      } catch (e) {
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000); // waiting out a rate limit window, not business time
      }
    }
  }
  /** The value cells (`dd`) carry the status badge and the payment line; they have the role `definition`. */
  value(text: string | RegExp): Locator { return this.page.getByRole("definition").filter({ hasText: text }); }
  cancelButton(): Locator { return this.page.getByRole("button", { name: "Zrezygnuj" }); }
  cancelDialog(): Locator { return this.page.getByRole("alertdialog", { name: "Czy na pewno zrezygnować z pakietu?" }); }
  confirmCancel(): Locator { return this.page.getByRole("button", { name: "Tak, zrezygnuj" }); }
  /**
   * Confirms the cancel and waits for the success line. A danger banner (the services answered 429/503/...) is logged (product copy only) and the confirm
   * is pressed again a few times: the cancel is all-or-nothing on the server, so a repeat after a failure cannot cancel twice.
   */
  async confirmCancelAndWait() {
    const done = this.page.getByText("Zrezygnowano z pakietu.");
    for (let attempt = 1; ; attempt++) {
      await this.confirmCancel().click();
      try {
        await expect(done).toBeVisible({ timeout: 10_000 });
        return;
      } catch (e) {
        const banners = await this.page.getByRole("alert").allInnerTexts();
        console.log(`[cancel attempt ${attempt}] banner: ${banners.join(" | ") || "none"}`);
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000); // waiting out a rate limit window, not business time
      }
    }
  }
  /** "wykorzystano X z Y" (tasks) or "... tys. tokenów" (chat) next to the bar. */
  usage(): Locator { return this.page.getByText(/^wykorzystano /); }
  /** The task form (a Confirmed task package that is not used up) and the list of tasks with state and result. */
  taskInput(): Locator { return this.page.getByLabel("Treść zadania"); }
  taskSubmit(): Locator { return this.page.getByRole("button", { name: "Zleć zadanie" }); }
  tasks(): Locator { return this.page.getByRole("list", { name: "Lista zadań" }).getByRole("listitem"); }
  async orderTask(text: string) {
    await this.taskInput().fill(text);
    await this.taskSubmit().click();
    await expect(this.page.getByText("Zadanie zostało zlecone. Wynik pojawi się poniżej.")).toBeVisible();
  }
  /**
   * Waits until the text of an answer is on the page. The page says it refreshes itself while a task is queued or running, but a meta refresh does not fire after the enhanced redirect that
   * follows the order, and it does not reload an address with a fragment (#zadania); what a client can always do is "Odśwież listę", so that is what this does until the answer is there.
   */
  async waitForAnswer(text: string) {
    await expect(async () => {
      if (!(await this.page.getByText(text).first().isVisible())) await this.page.getByRole("link", { name: "Odśwież listę" }).first().click();
      await expect(this.page.getByText(text).first()).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 60_000, intervals: [1_000, 2_000, 5_000] });
  }
  reviewLink(): Locator { return this.page.getByRole("link", { name: "Wystaw opinię" }); }
  disputeButton(): Locator { return this.page.getByRole("button", { name: "Zgłoś reklamację" }); }
  async openDispute(text: string) {
    await this.disputeButton().click();
    await this.page.getByLabel("Opis reklamacji").fill(text);
    await this.page.getByRole("button", { name: "Wyślij zgłoszenie" }).click();
  }
}

/** /moje/klucze: the client's API keys. The key is in the response to the creating POST only, once. */
export class ApiKeysPage {
  constructor(readonly page: Page) {}
  async goto() {
    await gotoInteractive(this.page, "/moje/klucze");
    await expect(this.page.getByRole("heading", { level: 1, name: "Klucze API" })).toBeVisible();
  }
  /** Creates a key through the form and returns the full key shown in that response (never log it). */
  async create(name: string): Promise<string> {
    await this.page.getByLabel("Nazwa klucza").fill(name);
    await this.page.getByRole("button", { name: "Utwórz klucz" }).click();
    const shown = this.page.getByTestId("new-key-value"); // a bare <code> labelled by the paragraph above it; no role to find it by
    await expect(shown).toBeVisible();
    return (await shown.textContent())!.trim();
  }
  /** The list rows hold the prefix only. */
  rows(): Locator { return this.page.getByRole("table", { name: "Lista kluczy API" }).getByRole("row"); }
}

export class AdminDisputesPage {
  constructor(readonly page: Page) {}
  async goto() {
    await gotoInteractive(this.page, "/admin/spory");
    await expect(this.page.getByRole("heading", { level: 1, name: "Reklamacje" })).toBeVisible();
  }
  /** One dispute card: the list item that names the booking id. */
  item(bookingId: string): Locator { return this.page.getByRole("listitem").filter({ hasText: bookingId }); }
  async resolvePartially(bookingId: string, amountPln: string, note: string) {
    const item = this.item(bookingId);
    await item.getByRole("button", { name: "Rozstrzygnij" }).click();
    await item.getByRole("radio", { name: "Zwróć klientowi część" }).check();
    await item.getByLabel("Kwota zwrotu w zł").fill(amountPln);
    await item.getByLabel("Notatka").fill(note);
    await item.getByRole("button", { name: "Przejdź do potwierdzenia" }).click();
  }
  confirmButton(bookingId: string): Locator { return this.item(bookingId).getByRole("button", { name: "Potwierdź rozstrzygnięcie" }); }
  /** Confirms and waits until the dispute leaves the open list; a danger banner is logged (product copy only) and the confirm pressed again a few times. */
  async confirmAndWait(bookingId: string) {
    for (let attempt = 1; ; attempt++) {
      const button = this.confirmButton(bookingId);
      if (await button.isVisible()) await button.click();
      try {
        await expect(this.item(bookingId)).toHaveCount(0, { timeout: 10_000 });
        return;
      } catch (e) {
        const banners = await this.item(bookingId).getByRole("alert").allInnerTexts();
        console.log(`[resolve attempt ${attempt}] banner: ${banners.join(" | ") || "none"}`);
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000);
      }
    }
  }
}
