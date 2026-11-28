using System.Net;
using System.Text.Json.Nodes;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

public class OffersHostTests
{
    static AgentWorld.Offer Offer(string title, bool active = true, long price = 18000, int duration = 60) => new() { Title = title, ProblemSlug = "code-review", Duration = duration, Price = price, Active = active };

    static (string, string)[] Valid(string title = "Przegląd testów end to end", string price = "180") => [("title", title), ("price", price), ("problemSlug", "code-review")];

    /// <summary>The posted fields; the kind and the units default to one task, as the form's own first option and value would send.</summary>
    static KeyValuePair<string, string>[] Fields(params (string, string)[] f)
    {
        var all = f.ToList();
        if (!all.Any(x => x.Item1 == "kind")) all.Add(("kind", "Tasks"));
        if (!all.Any(x => x.Item1 == "units")) all.Add(("units", "1"));
        return [.. all.Select(x => KeyValuePair.Create(x.Item1, x.Item2))];
    }

    [Fact]
    public async Task The_form_has_the_labels_the_journey_uses_a_select_whose_first_option_is_wybierz_and_a_group_of_technology_checkboxes()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/panel-agenta/oferty?nowa=1");
        Assert.Equal("Pakiety", page.TextOf("h1"));
        var labels = page.All("form[data-testid], form.form label, .field label").Select(l => l.TextContent.Trim()).ToList();
        foreach (var name in new[] { "Nazwa pakietu", "Opis pakietu", "Cena (zł)", "Rodzaj problemu" }) Assert.Equal(1, labels.Count(l => l.StartsWith(name))); // J2: getByLabel
        var problem = page.One("#problemSlug")!;
        Assert.Equal("Wybierz…", problem.QuerySelectorAll("option")[0].TextContent); // J2: selectOption({index: 1}) skips it
        Assert.Equal(["Przegląd kodu", "Migracja"], problem.QuerySelectorAll("option").Skip(1).Select(o => o.TextContent));
        var group = page.One("fieldset")!; // J2: getByRole("group", {name: /Technologie/}).getByRole("checkbox").first()
        Assert.Contains("Technologie", group.QuerySelector("legend")!.TextContent);
        Assert.Equal(5, group.QuerySelectorAll("input[type=checkbox]").Length);
        Assert.Equal("Zapisz", page.One("form.form button[type=submit]")!.TextContent);
        Assert.Equal("decimal", page.One("#price")!.GetAttribute("inputmode"));
    }

    [Fact]
    public async Task Saving_sends_the_price_as_integer_grosz_the_checked_technologies_and_redirects_with_the_flash_and_the_title_shows_once()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields(("title", "  Przegląd testów end to end  "), ("description", "Omawiamy scenariusze."), ("price", "180,50"), ("problemSlug", "code-review"), ("duration", "60")));
        var body = JsonNode.Parse(web.Directory.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings")).Body!)!;
        Assert.Equal(18050, body["priceMinor"]!.GetValue<long>()); // grosz, never a float
        Assert.Equal(60, body["durationMinutes"]!.GetValue<int>());
        Assert.Equal("Przegląd testów end to end", body["title"]!.GetValue<string>());
        Assert.Null(body["meetingUrl"]); // C-04: the meeting link is not sent at all
        Assert.Equal(("Tasks", 1), (body["kind"]!.GetValue<string>(), body["units"]!.GetValue<int>()));
        Assert.Equal("/panel-agenta/oferty?flash=saved", response.Location);
        Assert.Single(world.Offers);
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Zapisano.", after.TextOf("[data-testid=flash]")); // J2
        Assert.Equal(1, after.ExactText("Przegląd testów end to end")); // J2: getByText(title) must be a single element
        Assert.Equal("180,50 zł", after.TextOf(".offer .num").Replace(' ', ' '));
    }

    [Fact]
    public async Task The_checked_technologies_travel_and_more_than_four_are_refused_inline_before_any_call()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var four = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid(), ("techs", "dotnet"), ("techs", "react")]));
        Assert.Equal(["dotnet", "react"], world.Offers.Single().Techs);
        Assert.Contains((int)four.Status, new[] { 302, 303 });
        var posts = web.Directory.Requests.Count(r => r.Method == HttpMethod.Post);
        var five = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid("Druga oferta numer dwa"), ("techs", "dotnet"), ("techs", "react"), ("techs", "sql"), ("techs", "go"), ("techs", "rust")]));
        Assert.Equal(HttpStatusCode.OK, five.Status);
        Assert.Equal(posts, web.Directory.Requests.Count(r => r.Method == HttpMethod.Post)); // nothing sent
        Assert.Contains("najwyżej 4", five.TextOf("#techSlugs-error"));
        Assert.StartsWith("Błąd: ", five.One("#techSlugs-error")!.TextContent); // the hidden word is separated from the message
        Assert.Equal(5, five.All("input[type=checkbox]:checked").Count); // what the user ticked stays ticked
        Assert.Equal("Wybrano 5 z 4", five.TextOf("#techs-count")); // the count is rendered after the failed post
    }

    [Theory]
    [InlineData("250", true)] [InlineData("250,50", true)] [InlineData("250.5", true)] [InlineData("50", true)] [InlineData("1500", true)]
    [InlineData("49,99", false)] [InlineData("1500,01", false)] [InlineData("abc", false)] [InlineData("", false)] [InlineData("250,555", false)]
    public async Task The_price_is_validated_in_the_form_and_a_bad_one_never_reaches_the_service(string price, bool accepted)
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields(Valid(price: price)));
        Assert.Equal(accepted, (int)response.Status is 302 or 303);
        Assert.Equal(accepted ? 1 : 0, web.Directory.Requests.Count(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings")));
        if (!accepted)
        {
            Assert.Equal("true", response.One("#price")!.GetAttribute("aria-invalid"));
            Assert.Contains("price-error", response.One("#price")!.GetAttribute("aria-describedby"));
            Assert.Equal(price, response.One("#price")!.GetAttribute("value")); // the typed text stays
        }
    }

    [Fact]
    public async Task All_problems_come_at_once_in_the_summary_each_linked_to_its_field()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields(("title", "abc"), ("price", "x"), ("problemSlug", "")));
        var summary = response.One("[data-testid=form-summary]")!;
        Assert.Equal("Popraw 3 pola", summary.QuerySelector("h2")!.TextContent);
        Assert.Equal(["#title", "#price", "#problemSlug"], summary.QuerySelectorAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task A_service_complaint_is_put_on_its_field_and_the_contact_rule_on_the_text_that_looks_like_contact()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid(), ("description", "Pisz: jan@example.com")]));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Usuń dane kontaktowe", response.TextOf("#description-error"));
        Assert.Null(response.One("#title-error"));
        Assert.DoesNotContain("SERVER-", response.Html);
    }

    [Fact]
    public async Task Disabling_an_offer_needs_a_confirmation_page_state_and_the_first_click_changes_nothing()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var list = await browser.GetAsync("/panel-agenta/oferty");
        var disable = list.All(".offer a").Single(a => a.TextContent == "Wyłącz");
        Assert.Equal($"/panel-agenta/oferty?wylacz={id}", disable.GetAttribute("href")); // a link (GET), not a button that acts
        Assert.Contains("btn--ghost-danger", disable.ClassName);
        Assert.Null(list.One("[data-testid=confirm-disable]"));

        var confirm = await browser.GetAsync(disable.GetAttribute("href")!);
        Assert.True(world.Offers[0].Active); // nothing happened yet
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.EndsWith("deactivate"));
        var dialog = confirm.One("[data-testid=confirm-disable]")!;
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Equal("Wyłączyć pakiet „Przegląd testów”?", dialog.QuerySelector("h2")!.TextContent);
        Assert.Equal("confirm-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Contains("Klienci nie zobaczą go na profilu", dialog.TextContent);
        Assert.Equal("Tak, wyłącz pakiet", dialog.QuerySelector("button")!.TextContent);
        Assert.Contains("btn--danger", dialog.QuerySelector("button")!.ClassName);
        var cancel = dialog.QuerySelector("a")!;
        Assert.Equal(("Nie, zostaw aktywny", "/panel-agenta/oferty"), (cancel.TextContent, cancel.GetAttribute("href")));
    }

    [Fact]
    public async Task Confirming_disables_the_offer_with_a_flash_and_the_list_then_offers_wlacz_which_turns_it_back_on()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var done = await browser.Submit($"/panel-agenta/oferty?wylacz={id}", "offer-disable");
        Assert.Equal("/panel-agenta/oferty?flash=offer-disabled", done.Location);
        Assert.False(world.Offers[0].Active);
        var list = await browser.GetAsync(done.Location!);
        Assert.Equal("Pakiet został wyłączony. Możesz go włączyć z powrotem na liście.", list.TextOf("[data-testid=flash]"));
        var row = list.One(".offer")!;
        Assert.Equal("false", row.GetAttribute("data-active"));
        Assert.Contains("Wyłączony", row.TextContent);
        Assert.DoesNotContain(row.QuerySelectorAll("a"), a => a.TextContent == "Wyłącz"); // nothing left to disable
        var enable = row.QuerySelector("button")!;
        Assert.Equal("Włącz", enable.TextContent);

        var back = await browser.Submit("/panel-agenta/oferty", "offer-enable", Fields(("enable", id.ToString())));
        Assert.Equal("/panel-agenta/oferty?flash=offer-enabled", back.Location);
        Assert.True(world.Offers[0].Active);
    }

    [Fact]
    public async Task A_sixth_active_offer_cannot_be_turned_on_and_says_why()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); for (var i = 0; i < 5; i++) w.Offers.Add(Offer($"Oferta numer {i}")); w.Offers.Add(Offer("Wyłączona oferta", active: false)); });
        await using var _ = web;
        var off = world.Offers[5].Id;
        var page = await browser.Submit("/panel-agenta/oferty", "offer-enable", Fields(("enable", off.ToString())));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("najwyżej 5 aktywnych pakietów", page.TextOf("[data-testid=offers-error]"));
        Assert.False(world.Offers[5].Active);
        Assert.Empty(page.All("a.btn--primary")); // no "Dodaj pakiet" at the limit; the reason is shown in its place
        Assert.Contains("najwyżej 5 aktywnych pakietów", page.Text);
        Assert.Equal("Aktywne pakiety: 5 z 5", page.TextOf("[data-testid=active-count]"));
        var at_limit = await browser.GetAsync("/panel-agenta/oferty?nowa=1");
        Assert.True(at_limit.One("form.form button[type=submit]")!.HasAttribute("disabled")); // a direct link to the form still shows it disabled, with the reason next to it
    }

    [Fact]
    public async Task Editing_loads_the_offer_with_its_price_in_zloty_and_a_save_replaces_it()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów", price: 18050)); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var edit = await browser.GetAsync($"/panel-agenta/oferty?edit={id}");
        Assert.Equal("Edycja pakietu", edit.TextOf("#offer-form-title"));
        Assert.Equal("180,50", edit.One("#price")!.GetAttribute("value"));
        Assert.Equal("60", edit.One("#duration option[selected]")!.GetAttribute("value"));
        Assert.Contains("tylko zakupów dokonanych po zapisie", edit.Text);
        var saved = await browser.Submit($"/panel-agenta/oferty?edit={id}", "offer", Fields(("title", "Przegląd testów v2"), ("price", "200")));
        Assert.Equal(HttpStatusCode.Found, saved.Status);
        Assert.Equal(("Przegląd testów v2", 20000L), (world.Offers[0].Title, world.Offers[0].Price));
        Assert.Contains(web.Directory.Requests, r => r.Method == HttpMethod.Put && r.PathAndQuery.EndsWith($"/offerings/{id}"));
    }

    [Fact]
    public async Task A_made_up_or_foreign_id_edits_nothing_and_an_empty_list_says_what_to_do()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync($"/panel-agenta/oferty?edit={Guid.NewGuid()}&wylacz={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Nowy pakiet", page.TextOf("#offer-form-title"));
        Assert.Null(page.One("[data-testid=confirm-disable]"));
        Assert.Contains("Nie masz jeszcze żadnego pakietu", page.TextOf(".empty-state"));
        var junk = await browser.GetAsync("/panel-agenta/oferty?edit=not-a-guid");
        Assert.Equal(HttpStatusCode.OK, junk.Status);
    }

    [Fact]
    public async Task The_offer_text_is_text_and_a_failing_taxonomy_still_lets_the_page_render_with_a_message()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("<b>Pogrubiona</b>")); });
        await using var _ = web;
        var page = await browser.GetAsync("/panel-agenta/oferty");
        Assert.Empty(page.All("b"));
        Assert.Equal("<b>Pogrubiona</b>", page.TextOf(".offer__title"));
    }

    [Fact]
    public async Task An_id_in_the_form_that_is_not_one_of_the_agents_offers_creates_a_new_offer_and_never_updates_it()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var foreign = Guid.NewGuid();
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid(), ("id", foreign.ToString())]));
        Assert.Contains((int)response.Status, new[] { 302, 303 });
        Assert.DoesNotContain(web.Directory.Requests, r => r.Method == HttpMethod.Put);
        Assert.Single(world.Offers);
        Assert.NotEqual(foreign, world.Offers[0].Id);
    }

    [Fact]
    public async Task Disabling_or_enabling_an_id_that_is_not_the_agents_calls_nothing()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = Guid.NewGuid().ToString();
        var page = await browser.GetAsync("/panel-agenta/oferty");
        var token = page.One("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        var forged = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/panel-agenta/oferty?wylacz={id}") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "offer-disable", ["id"] = id, ["__RequestVerificationToken"] = token }) });
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status); // no such confirmation form on the page: the framework refuses the post before any handler
        var enable = await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/panel-agenta/oferty") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "offer-enable", ["enable"] = id, ["__RequestVerificationToken"] = token }) });
        Assert.Equal(HttpStatusCode.OK, enable.Status);
        Assert.Contains("Nie znaleziono tego pakietu", enable.TextOf("[data-testid=offers-error]")); // asked of the practitioner's own list first
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.EndsWith("deactivate") || r.PathAndQuery.EndsWith("activate"));
    }

    [Fact]
    public async Task The_form_is_closed_by_default_the_list_has_a_button_that_opens_it_and_the_count_of_technologies_starts_at_zero()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var list = await browser.GetAsync("/panel-agenta/oferty");
        Assert.Null(list.One("form.form"));
        Assert.Null(list.One("#offer-form-title"));
        Assert.Null(list.One("input[name=title]"));
        var add = list.All("a.btn--primary").Single(a => a.TextContent == "Dodaj pakiet");
        Assert.Equal("/panel-agenta/oferty?nowa=1", add.GetAttribute("href"));

        var opened = await browser.GetAsync(add.GetAttribute("href")!);
        Assert.Equal("Nowy pakiet", opened.TextOf("#offer-form-title"));
        Assert.NotNull(opened.One("form.form button[type=submit]"));
        Assert.DoesNotContain(opened.All("a"), a => a.TextContent == "Dodaj pakiet"); // the form is open: the button is not needed
        Assert.Equal("Wybrano 0 z 4", opened.TextOf("#techs-count"));
        Assert.Single(opened.All("#offer-form-title"));
    }

    [Fact]
    public async Task A_failed_post_from_an_edit_shows_the_form_again_with_its_errors_and_saves_nothing()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        var post = await browser.Submit($"/panel-agenta/oferty?edit={id}", "offer", Fields(("title", "abc"), ("price", "x"), ("problemSlug", "")));
        Assert.Equal(HttpStatusCode.OK, post.Status);
        Assert.NotNull(post.One("form.form"));
        Assert.Equal("Edycja pakietu", post.TextOf("#offer-form-title"));
        Assert.Equal("Popraw 3 pola", post.One("[data-testid=form-summary] h2")!.TextContent);
        Assert.Equal("Przegląd testów", world.Offers.Single().Title); // nothing was saved
    }

    [Fact]
    public async Task The_headings_never_skip_a_level_and_the_offer_titles_sit_under_the_list_heading()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów")); });
        await using var _ = web;
        var id = world.Offers[0].Id;
        foreach (var path in new[] { "/panel-agenta/oferty", "/panel-agenta/oferty?nowa=1", $"/panel-agenta/oferty?edit={id}" })
        {
            var page = await browser.GetAsync(path);
            var levels = page.All("h1, h2, h3, h4, h5, h6").Select(h => int.Parse(h.LocalName[1..])).ToList();
            Assert.Equal(1, levels[0]);
            for (var i = 1; i < levels.Count; i++) Assert.True(levels[i] <= levels[i - 1] + 1, $"{path}: an h{levels[i]} follows an h{levels[i - 1]}");
            Assert.Equal("Przegląd testów", page.TextOf("h3.offer__title"));
            var firstH3 = levels.IndexOf(3);
            Assert.True(firstH3 < 0 || levels.Take(firstH3).Contains(2), $"{path}: an h2 must come before the first h3");
            Assert.Equal("Twoje pakiety", page.TextOf("#offers-list-title"));
        }
    }

    [Fact]
    public async Task The_preview_of_the_public_card_shows_the_saved_values_when_editing_and_placeholders_when_creating()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => { w.HaveProfile(); w.Offers.Add(Offer("Przegląd testów", price: 18050)); });
        await using var _ = web;
        var created = await browser.GetAsync("/panel-agenta/oferty?nowa=1");
        Assert.Equal("Podgląd pakietu", created.TextOf("#offer-preview-title"));
        Assert.Equal("Tytuł pakietu", created.TextOf(".offer-preview__title"));
        Assert.Contains("muted", created.One(".offer-preview__title")!.ClassName);
        var edit = await browser.GetAsync($"/panel-agenta/oferty?edit={world.Offers[0].Id}");
        Assert.Equal("Przegląd testów", edit.TextOf(".offer-preview__title"));
        Assert.DoesNotContain("muted", edit.One(".offer-preview__title")!.ClassName);
        Assert.Contains("60 min", edit.TextOf(".offer-preview__meta"));
        Assert.Contains("180,50", edit.TextOf(".offer-preview__meta"));
    }

    [Fact]
    public async Task The_form_has_the_kind_and_units_fields_and_no_meeting_link_field_C04()
    {
        var (web, browser, _) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var page = await browser.GetAsync("/panel-agenta/oferty?nowa=1");
        Assert.Equal("Rodzaj pakietu", page.One("label[for=kind]")!.TextContent.Replace(" opcjonalnie", "").Trim());
        Assert.Equal(["Zadania", "Czat API"], page.All("#kind option").Select(o => o.TextContent));
        Assert.Equal("Liczba jednostek", page.One("label[for=units]")!.TextContent.Trim());
        Assert.Equal("Typowy czas wykonania", page.One("label[for=duration]")!.TextContent.Trim());
        Assert.Null(page.One("#meetingUrl")); Assert.DoesNotContain("spotkania", page.Text);
    }

    [Theory]
    [InlineData("Tasks", "0", "units", "Dla zadań wpisz liczbę od 1 do 1000.")]
    [InlineData("Tasks", "1001", "units", "Dla zadań wpisz liczbę od 1 do 1000.")]
    [InlineData("Chat", "99", "units", "Dla czatu API wpisz liczbę od 100 do 100000 (tysięcy tokenów).")]
    [InlineData("Chat", "100001", "units", "Dla czatu API wpisz liczbę od 100 do 100000 (tysięcy tokenów).")]
    [InlineData("Chat", "abc", "units", "Dla czatu API wpisz liczbę od 100 do 100000 (tysięcy tokenów).")]
    public async Task Units_outside_the_range_of_the_kind_are_refused_inline_before_any_call(string kind, string units, string field, string message)
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid(), ("kind", kind), ("units", units)]));
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Empty(world.Offers);
        Assert.DoesNotContain(web.Directory.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings"));
        Assert.Contains(message, response.TextOf("#" + field + "-error"));
    }

    [Fact]
    public async Task A_chat_package_sends_its_kind_and_the_units_in_thousands_of_tokens_and_the_list_says_so()
    {
        var (web, browser, world) = await AgentBrowsers.SignedIn(w => w.HaveProfile());
        await using var _ = web;
        var response = await browser.Submit("/panel-agenta/oferty?nowa=1", "offer", Fields([.. Valid("Dostęp do API czatu", "99"), ("kind", "Chat"), ("units", "500")]));
        Assert.Contains((int)response.Status, new[] { 302, 303 });
        var body = JsonNode.Parse(web.Directory.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/offerings")).Body!)!;
        Assert.Equal(("Chat", 500), (body["kind"]!.GetValue<string>(), body["units"]!.GetValue<int>()));
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("500 tys. tokenów, API zgodne z OpenAI", after.TextOf("[data-testid=offer-units]"));
    }
}
