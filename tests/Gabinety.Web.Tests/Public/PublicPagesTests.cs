using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.Public;

/// <summary>The wave-2 public pages as a visitor with no JavaScript gets them: complete HTML in the first response, the statuses, the hooks of DESIGN-CONTRACT section 9.</summary>
public class ProfilePageTests
{
    static async Task<Page> Get(WebFactory web, string path = "/agent/demo-anna-demowska") => await web.NewBrowser().GetAsync(path);

    [Fact]
    public async Task The_profile_has_its_content_the_hooks_and_the_structured_data_in_the_first_response()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Anna Ąćęł Demowska", page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.Equal(["O mnie", "Pakiety", "Jak się połączyć", "Opinie"], page.All("h2").Select(h => h.TextContent));
        Assert.Equal(["Audyt kodu .NET", "Krótka konsultacja"], page.All("ul.offerings li h3").Select(h => h.TextContent)); // getByRole("heading", {level: 3}) inside the offer's listitem
        var offer = page.All("ul.offerings > li")[0];
        // C-01: a package has no slot; the link goes to the purchase page of the package, which finds the package in this profile by the slug.
        Assert.Equal($"/dostep/{PublicSupport.OfferA}?gabinet=demo-anna-demowska", offer.QuerySelector("a[aria-label='Wykup dostęp: Audyt kodu .NET']")!.GetAttribute("href"));
        var ld = page.All("script[type='application/ld+json']"); // exactly one, with the display name (journey J1)
        Assert.Single(ld);
        Assert.Contains("Anna Ąćęł Demowska", ld[0].TextContent);
        Assert.Equal("ProfessionalService", JsonDocument.Parse(ld[0].TextContent).RootElement.GetProperty("@type").GetString());
        Assert.Equal("Anna Ąćęł Demowska: agent AI | Gabinety Tech", page.Dom.Title);
        Assert.Equal("http://localhost/agent/demo-anna-demowska", page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("index,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Contains("Audyty kodu .NET", page.One("meta[name=description]")!.GetAttribute("content"));
        Assert.Equal("reviews-title", page.One("section#opinie")!.GetAttribute("aria-labelledby")); // named by its heading: getByRole("region", {name: "Opinie"})
        Assert.Equal("Opinie", page.TextOf("#reviews-title"));
    }

    [Fact]
    public async Task The_book_button_is_Wykup_dostep_and_never_waits_for_a_slot_C01()
    {
        await using var web = PublicSupport.Host(access: FakeBackend.Throw(new HttpRequestException("down")));
        var page = await Get(web);
        var book = page.All("ul.offerings > li")[1].QuerySelector("a.btn--primary")!;
        Assert.Equal("Wykup dostęp", book.TextContent); Assert.Equal("Wykup dostęp: Krótka konsultacja", book.GetAttribute("aria-label"));
        Assert.Equal($"/dostep/{PublicSupport.OfferB}?gabinet=demo-anna-demowska", book.GetAttribute("href"));
        Assert.Empty(web.Access.Requests); // no slot is read: the Booking service is not asked at all
        Assert.Null(page.One(".slot-days")); Assert.Null(page.One("a.slot"));
    }

    [Fact]
    public async Task Each_package_shows_its_units_and_validity_and_a_chat_package_the_openai_line_C01()
    {
        await using var web = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(packages:
        [
            new(PublicSupport.OfferA, "Plan migracji", "Plan", 30, new MoneyDto(18000, "PLN"), "audyt-kodu", ["dotnet"], "Tasks", 10),
            new(PublicSupport.OfferB, "Dostęp do API czatu", "Czat", 30, new MoneyDto(9900, "PLN"), "audyt-kodu", [], "Chat", 500),
        ])));
        var offers = (await Get(web)).All("ul.offerings > li");
        Assert.Equal("10 zadań, typowy czas ok. 30 min", offers[0].QuerySelector("[data-testid=offering-units]")!.TextContent);
        Assert.Equal("500 tys. tokenów, API zgodne z OpenAI", offers[1].QuerySelector("[data-testid=offering-units]")!.TextContent);
        Assert.All(offers, o => Assert.Equal("Ważność: 30 dni", o.QuerySelector("[data-testid=offering-validity]")!.TextContent));
    }

    [Fact]
    public async Task The_kind_is_read_as_the_public_profile_writes_it_in_lower_case()
    {
        // the Directory's public JSON writes the kind camelCased ("tasks", "chat"); the page must not show a chat package as "500 zadań"
        await using var web = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(packages:
        [
            new(PublicSupport.OfferA, "Plan migracji", "Plan", 30, new MoneyDto(18000, "PLN"), "audyt-kodu", ["dotnet"], "tasks", 10),
            new(PublicSupport.OfferB, "Dostęp do API czatu", "Czat", 30, new MoneyDto(9900, "PLN"), "audyt-kodu", [], "chat", 500),
        ])));
        var offers = (await Get(web)).All("ul.offerings > li");
        Assert.Equal("10 zadań, typowy czas ok. 30 min", offers[0].QuerySelector("[data-testid=offering-units]")!.TextContent);
        Assert.Equal("500 tys. tokenów, API zgodne z OpenAI", offers[1].QuerySelector("[data-testid=offering-units]")!.TextContent);
    }

    [Theory]
    [InlineData(1, "zadanie")] [InlineData(2, "zadania")] [InlineData(4, "zadania")] [InlineData(5, "zadań")] [InlineData(12, "zadań")] [InlineData(22, "zadania")] [InlineData(25, "zadań")]
    public void Task_word_follows_polish_plural_rules(int n, string word) => Assert.Equal(word, Gabinety.Web.Components.Pages.Gabinet.Profile.TaskWord(n));

    [Fact]
    public async Task The_profile_carries_the_AI_badge_and_the_how_to_connect_section_with_the_slug_and_an_example_C01()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        Assert.Equal("Agent AI", page.One("header.page-header [data-testid=ai-badge]")!.TextContent);
        var connect = page.One("section[data-testid=connect]")!;
        Assert.Equal("Jak się połączyć", connect.QuerySelector("h2")!.TextContent);
        Assert.Equal("demo-anna-demowska", connect.QuerySelector("[data-testid=connect-model]")!.TextContent);
        Assert.Equal("http://localhost/v1", connect.QuerySelector("[data-testid=connect-base-url]")!.TextContent); // Site:BaseUrl + /v1: the site forwards /v1 (ADR-0013)
        var curl = connect.QuerySelector("[data-testid=connect-curl]")!.TextContent;
        Assert.Contains("/v1/chat/completions", curl); Assert.Contains("\"model\":\"demo-anna-demowska\"", curl); Assert.Contains("TWÓJ_KLUCZ_API", curl);
        Assert.DoesNotContain("gab_", curl); // a placeholder, never a key
    }

    [Fact]
    public async Task Header_has_one_success_badge_the_other_checks_in_details_and_quiet_links()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        var header = page.One("header.page-header")!;
        Assert.Equal(["Zatwierdzony"], header.QuerySelectorAll(".badge--success").Select(b => b.TextContent));
        Assert.NotNull(header.QuerySelector(".avatar--large"));
        Assert.Equal("Nowy", header.QuerySelector("ul[aria-label=Weryfikacje] .badge:not(.badge--success)")!.TextContent);
        var checks = header.QuerySelector("details.checks")!;
        Assert.Equal("Co sprawdziliśmy", checks.QuerySelector("summary")!.TextContent);
        Assert.Equal(["E-mail zweryfikowany", "GitHub połączony", "NIP zweryfikowany"], checks.QuerySelectorAll("li strong").Select(b => b.TextContent));
        Assert.Empty(header.QuerySelectorAll("a.btn"));
        Assert.Equal(["Portfolio", "GitHub"], header.QuerySelectorAll("a.link").Select(a => a.TextContent));

        await using var rated = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(isNew: false, rating: new RatingDto(4.8, 25))));
        var r = await Get(rated);
        Assert.Equal("★ 4,8 (25)", r.TextOf("header .rating"));
        Assert.DoesNotContain("Nowy", r.TextOf("header ul[aria-label=Weryfikacje]"));
    }

    [Fact]
    public async Task Offers_show_technology_tags_from_the_taxonomy_and_fail_soft_without_it()
    {
        await using var web = PublicSupport.Host();
        var tags = (await Get(web)).All("ul.offerings > li")[0].QuerySelectorAll(".tags .badge--neutral");
        Assert.Equal([".NET"], tags.Select(t => t.TextContent));

        var down = PublicSupport.DefaultDirectory();
        await using var failing = PublicSupport.Host(r => r.RequestUri!.AbsolutePath == "/api/v1/taxonomy" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : down(r));
        var page = await Get(failing);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["dotnet"], page.All("ul.offerings > li")[0].QuerySelectorAll(".tags .badge--neutral").Select(t => t.TextContent)); // the slug is the fallback text
    }

    [Fact]
    public async Task The_summary_panel_shows_price_the_link_to_the_first_offer_and_the_payment_line()
    {
        await using var web = PublicSupport.Host();
        var aside = (await Get(web)).One("aside.summary")!;
        Assert.Contains("od", aside.TextContent); Assert.Contains("110", aside.TextContent);
        Assert.Equal($"#oferta-{PublicSupport.OfferA}", aside.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Zobacz pakiety", aside.QuerySelector("a")!.TextContent);
        Assert.Contains("Płatność z góry, pełny zwrot do pierwszego użycia pakietu", aside.TextContent);

        await using var none = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(packages: [])));
        Assert.Null((await Get(none)).One("aside.summary"));
    }

    [Theory]
    [InlineData(1, "opinia")] [InlineData(2, "opinie")] [InlineData(4, "opinie")] [InlineData(5, "opinii")] [InlineData(12, "opinii")] [InlineData(22, "opinie")] [InlineData(25, "opinii")]
    public void Opinion_word_follows_polish_plural_rules(int n, string word) => Assert.Equal(word, Gabinety.Web.Components.Pages.Gabinet.Profile.OpinionWord(n));

    [Fact]
    public async Task Reviews_have_a_summary_line_above_the_list_when_rated()
    {
        var review = new ReviewDto(Guid.NewGuid(), 5, "Super", new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), null);
        await using var web = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(isNew: false, rating: new RatingDto(4.8, 25))), reputation: _ => FakeBackend.Json(new PageDto<ReviewDto>([review], null)));
        var section = (await Get(web)).One("section#opinie")!;
        Assert.Equal("★ 4,8 · 25 opinii", section.QuerySelector("[data-testid=reviews-summary]")!.TextContent);
        Assert.Contains("Ocena: 5 z 5", section.QuerySelector("ul.reviews li")!.TextContent);
        Assert.Equal("reviews-summary", section.QuerySelector("[data-testid=reviews-summary] ~ ul.reviews") is null ? "" : "reviews-summary");
    }

    [Fact]
    public async Task F16_a_profile_with_no_packages_says_so_and_the_booking_service_is_not_asked()
    {
        await using var none = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(packages: [])));
        var noOffers = await Get(none);
        Assert.Equal("Ten agent nie ma teraz aktywnych pakietów.", noOffers.TextOf("[data-testid=no-offerings]"));
        Assert.Empty(none.Access.Requests);
    }

    [Fact]
    public async Task F08_member_since_reads_od_listopada_and_the_facts_are_separate_items()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web);
        Assert.Equal(["Zrealizowane pakiety: 4", "Na platformie od listopada 2026"], page.All("ul.facts li").Select(l => l.TextContent));
        Assert.DoesNotContain("Listopada", page.Text); // no capitalised month anywhere
    }

    [Fact]
    public async Task Reviews_show_text_as_text_the_reply_and_a_more_link_and_one_empty_sentence()
    {
        var review = new ReviewDto(Guid.NewGuid(), 5, "<b>Świetnie</b> & szybko", new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), new ReplyDto("Dziękuję!", new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));
        await using var web = PublicSupport.Host(reputation: r => FakeBackend.Json(new PageDto<ReviewDto>([review], "CUR2")));
        var page = await Get(web, "/agent/demo-anna-demowska?opinie=CUR1");
        Assert.Equal($"/api/v1/practitioners/{PublicSupport.AgentId}/reviews?cursor=CUR1", web.Reputation.Requests[0].PathAndQuery);
        var item = page.One("section#opinie li")!;
        Assert.Contains("Zweryfikowany klient · Ocena: 5 z 5 · 20 listopada 2026", item.TextContent);
        Assert.Contains("<b>Świetnie</b> & szybko", item.TextContent);
        Assert.Null(item.QuerySelector("b")); // review text is never markup
        Assert.Contains("Dziękuję!", item.QuerySelector(".reply")!.TextContent);
        Assert.Equal("/agent/demo-anna-demowska?opinie=CUR2#opinie", page.One("section#opinie a.btn")!.GetAttribute("href"));

        await using var none = PublicSupport.Host();
        var empty = await Get(none);
        Assert.Single(empty.All("section#opinie p")); // P-PROF-1: one sentence, not two
        Assert.Equal("Nowy na platformie: ten agent nie ma jeszcze opinii.", empty.TextOf("section#opinie p"));
    }

    [Fact]
    public async Task A_reviews_outage_is_a_warning_in_that_region_only()
    {
        await using var web = PublicSupport.Host(reputation: FakeBackend.Throw(new HttpRequestException("down")));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Opinie są chwilowo niedostępne", page.TextOf("[data-testid=reviews-unavailable]"));
        Assert.NotNull(page.One("ul.offerings"));
    }

    [Fact]
    public async Task Links_are_https_only_and_external_ones_carry_rel()
    {
        await using var web = PublicSupport.Host(PublicSupport.DefaultDirectory(PublicSupport.Profile(portfolio: "javascript:alert(1)")));
        var page = await Get(web);
        Assert.Null(page.One("a[href^=javascript]"));
        Assert.Null(page.One("a[href*='demo.example']"));
        var github = page.One("a[href='https://github.com/demo-anna']")!;
        Assert.Contains("nofollow", github.GetAttribute("rel"));
        await using var ok = PublicSupport.Host();
        Assert.Equal("noopener nofollow ugc", (await Get(ok)).One("a[href='https://demo.example/portfolio']")!.GetAttribute("rel"));
    }

    [Fact]
    public async Task F09_an_unknown_profile_is_a_404_that_says_programmer_not_page_and_offers_the_way_on()
    {
        await using var web = PublicSupport.Host();
        var page = await Get(web, "/agent/nie-ma-takiego");
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Equal("Nie znaleziono agenta", page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.Equal("/agenci", page.One("[data-testid=profile-not-found] a.btn")!.GetAttribute("href"));
        Assert.Equal(["Audyt kodu", "Migracja"], page.All("[data-testid=profile-not-found] .chip").Select(c => c.TextContent));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.Null(page.One("script[type='application/ld+json']"));
    }

    [Fact]
    public async Task A_profile_the_directory_calls_gone_is_a_410()
    {
        await using var web = PublicSupport.Host(r => r.RequestUri!.AbsolutePath.StartsWith("/api/v1/practitioners/") ? FakeBackend.Problem(HttpStatusCode.Gone, "gone") : PublicSupport.DefaultDirectory()(r));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.Gone, page.Status);
        Assert.Equal("Nie znaleziono agenta", page.TextOf("h1"));
    }

    [Fact]
    public async Task A_directory_that_is_down_is_a_503_not_a_200_error_page()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var page = await Get(web);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
        Assert.Equal("noindex,follow", page.One("meta[name=robots]")!.GetAttribute("content"));
        Assert.DoesNotContain("SERVER-", page.Html);
    }
}

public class StaticPagesTests
{
    [Theory]
    [InlineData("/jak-to-dziala", "Jak to działa")] [InlineData("/cennik", "Cennik")] [InlineData("/zasady-rankingu", "Zasady rankingu")]
    [InlineData("/regulamin", "Regulamin")] [InlineData("/polityka-prywatnosci", "Polityka prywatności")]
    public async Task A_static_page_has_one_h1_a_unique_title_and_description_and_a_canonical(string path, string h1)
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(h1, page.TextOf("h1")); Assert.Single(page.All("h1"));
        Assert.EndsWith("| Gabinety Tech", page.Dom.Title);
        Assert.NotEmpty(page.One("meta[name=description]")!.GetAttribute("content")!);
        Assert.Equal("http://localhost" + path, page.One("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Empty(page.SetCookies);
    }

    [Fact]
    public async Task Pricing_shows_the_rate_and_the_example_split_and_the_rate_is_configuration()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/cennik");
        Assert.Contains("Prowizja platformy to 15% ceny", page.Text);
        var cells = page.All("section[aria-labelledby=pricing-example] tbody td").Select(c => c.TextContent.Replace(' ', ' '));
        Assert.Equal(["120,00 zł", "18,00 zł", "102,00 zł"], page.All("section[aria-labelledby=pricing-example-2] tbody td").Select(c => c.TextContent.Replace('\u00A0', ' ')));  // 15% of 12 000 gr = 1 800 gr
        Assert.Equal(["250,00 zł", "37,50 zł", "212,50 zł"], cells);
        await using var ten = PublicSupport.Host(); ten.With("Billing:FirstConsultationFeeBps", "1000");
        var other = await ten.NewBrowser().GetAsync("/cennik");
        Assert.Contains("Prowizja platformy to 10% ceny", other.Text);
        Assert.Equal(["250,00 zł", "25,00 zł", "225,00 zł"], other.All("section[aria-labelledby=pricing-example] tbody td").Select(c => c.TextContent.Replace(' ', ' ')));
        Assert.Equal(["120,00 zł", "12,00 zł", "108,00 zł"], other.All("section[aria-labelledby=pricing-example-2] tbody td").Select(c => c.TextContent.Replace('\u00A0', ' ')));
    }

    [Fact]
    public async Task F12_the_ranking_page_shows_every_value_the_total_row_and_a_keyboard_reachable_scroller()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/zasady-rankingu");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("82,70", page.TextOf("[data-testid=ranking-total]")); // the Wynik razem row has its value
        Assert.Equal("Wynik razem", page.One("tfoot th")!.TextContent);
        var constants = page.All("section[aria-labelledby=rank-constants] tbody tr").Select(r => r.QuerySelector("td")!.TextContent);
        Assert.Equal(["5", "3,8", "3", "10"], constants); // the Stałe table shows values
        Assert.Equal(["45%", "25%", "15%", "15%"], page.All("section[aria-labelledby=rank-weights] tbody td.num").Select(c => c.TextContent));
        Assert.Contains("Wersja zasad: 2026-11-v1", page.Text);
        Assert.Contains("Obowiązuje od: 13.11.2026", page.Text);
        var scrollers = page.All(".table-wrap");
        Assert.Equal(3, scrollers.Count);
        Assert.All(scrollers, s => { Assert.Equal("0", s.GetAttribute("tabindex")); Assert.Equal("region", s.GetAttribute("role")); Assert.False(string.IsNullOrWhiteSpace(s.GetAttribute("aria-label"))); });
    }

    [Fact]
    public async Task The_ranking_page_is_a_503_and_never_shows_stale_numbers_when_the_directory_is_down()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        var page = await web.NewBrowser().GetAsync("/zasady-rankingu");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, page.Status);
        Assert.Equal("Zasady rankingu", page.TextOf("h1"));
        Assert.Empty(page.All("table"));
        Assert.Equal("alert", page.One("[data-testid=unavailable]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task Legal_pages_render_the_owners_text_with_a_contents_list_and_the_draft_notice()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/regulamin");
        Assert.Contains("Wersja robocza", page.TextOf("[data-testid=draft-notice]"));
        Assert.Equal(["1. Postanowienia ogólne", "2. Wykup dostępu i płatność", "3. Klucze API", "4. Reklamacje", "5. Prowizja"], page.All("nav.contents a").Select(a => a.TextContent));
        Assert.Equal("s-1-postanowienia-ogolne", page.One("nav.contents a")!.GetAttribute("href")![1..]);
        Assert.NotNull(page.One("h3#s-1-postanowienia-ogolne"));
        Assert.Empty(page.All("script:not([src])")); // the only script is the framework's own file
    }

    [Fact]
    public async Task The_home_page_has_tiles_the_trust_row_and_still_the_hooks()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/");
        Assert.Equal(["Audyt kodu", "Migracja"], page.All("[data-testid=categories] a").Select(a => a.TextContent));
        Assert.Equal(2, page.All("[data-testid=categories] li.card").Count);
        Assert.Contains("6 zatwierdzonych agentów", page.TextOf("[data-testid=trust]"));
        Assert.Contains("Płatność z góry, pełny zwrot do pierwszego użycia pakietu", page.TextOf("[data-testid=trust]"));
        Assert.Equal(3, page.All("ol.steps > li").Count);
        Assert.Equal("Szukaj", page.One("form[role=search] button")!.TextContent); // the field and the button in one form row (G-7)
    }
}

public class ListPageFixesTests
{
    static WebFactory Host(int items, bool next, bool known = true) => PublicSupport.Host(r => r.RequestUri!.AbsolutePath switch
    {
        "/api/v1/practitioners" => FakeBackend.Json(Sample.Cards(items, next ? "NEXT" : null)),
        _ => PublicSupport.DefaultDirectory()(r),
    });

    [Fact]
    public async Task F05_an_empty_filtered_result_says_zero_and_never_the_categorys_total()
    {
        await using var web = Host(0, false);
        var page = await web.NewBrowser().GetAsync("/agenci?q=zzzzzz");
        Assert.Equal("Wyniki: 0", page.TextOf("[data-testid=count]"));
        Assert.Contains("Nie ma agentów dla tych filtrów", page.TextOf("[data-testid=empty]"));
        Assert.Equal("/agenci", page.One(".empty-state a.btn--ghost")!.GetAttribute("href")); // Wyczyść filtry, shown because a filter is active
        Assert.Empty(page.All("[data-testid=practitioner-list]"));
    }

    [Fact]
    public async Task F05_the_count_of_a_filtered_whole_result_is_its_items_and_an_open_ended_one_states_none()
    {
        await using var whole = Host(2, false);
        Assert.Equal("Wyniki: 2", (await whole.NewBrowser().GetAsync("/agenci?duration=30")).TextOf("[data-testid=count]"));
        await using var more = Host(2, true);
        Assert.Null((await more.NewBrowser().GetAsync("/agenci?duration=30")).One("[data-testid=count]"));
        await using var plain = Host(2, true);
        Assert.Equal("Wyniki: 6", (await plain.NewBrowser().GetAsync("/agenci")).TextOf("[data-testid=count]")); // unfiltered: the sitemap's count
    }

    [Fact]
    public async Task P_LIST_the_filters_are_one_disclosure_with_segmented_duration_and_price_and_a_breadcrumb_on_a_category()
    {
        await using var web = Host(2, false);
        var page = await web.NewBrowser().GetAsync("/agenci/audyt-kodu/dotnet?duration=30");
        Assert.Equal("Filtry (3)", page.TextOf("details.filters > summary")); // problem, technology and duration are active
        Assert.NotNull(page.One("details.filters[open] nav[aria-label=Filtry]")); // the navigation keeps its name (hook)
        Assert.Equal(2, page.All("nav[aria-label=Filtry] ul.segmented").Count);
        Assert.Equal(["Agenci", "Audyt kodu", ".NET"], page.All("nav.breadcrumb li").Select(l => l.TextContent.Trim()));
        Assert.Equal("page", page.One("nav.breadcrumb [aria-current]")!.GetAttribute("aria-current"));
    }

    [Fact]
    public async Task P_LIST_2_a_card_shows_one_verified_badge_not_four_and_the_rating_as_a_star()
    {
        await using var web = PublicSupport.Host(r => r.RequestUri!.AbsolutePath == "/api/v1/practitioners"
            ? FakeBackend.Json(new PageDto<AgentCardDto>([Sample.Card(rating: new RatingDto(4.8, 25), isNew: false)], null)) : PublicSupport.DefaultDirectory()(r));
        var card = (await web.NewBrowser().GetAsync("/agenci")).One("[data-testid=practitioner-card]")!;
        Assert.Equal(["Agent AI", "Zweryfikowany"], card.QuerySelectorAll(".badge").Select(b => b.TextContent));
        Assert.Equal("★ 4,8 (25)", card.QuerySelector(".practitioner-card__rating")!.TextContent);
    }
}

public class SeoEndpointsTests
{
    [Fact]
    public async Task The_sitemap_lists_every_approved_practitioner_and_only_the_indexable_categories()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("application/xml", page.Header("Content-Type"));
        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var urls = XDocument.Parse(page.Html).Root!.Elements(ns + "url").ToList(); // parses: well-formed XML
        Assert.Equal(["http://localhost/agenci", "http://localhost/agenci/audyt-kodu", "http://localhost/agent/demo-anna-demowska", "http://localhost/agent/demo-bartek"], urls.Select(u => u.Element(ns + "loc")!.Value));
        Assert.DoesNotContain("/agenci/migracja", page.Html); // 2 practitioners: noindex, so not offered
        Assert.Equal("2026-11-20T08:30:00.000Z", urls[2].Element(ns + "lastmod")!.Value);
        Assert.Null(urls[0].Element(ns + "lastmod"));
    }

    [Fact]
    public void The_sitemap_escapes_what_a_slug_could_carry()
    {
        var xml = SeoContentProbe.Sitemap(new SitemapDto([], [new("a&b<c>", DateTimeOffset.UnixEpoch)]));
        Assert.NotNull(XDocument.Parse(xml)); // still well-formed
        Assert.DoesNotContain("a&b<c>", xml);
    }

    [Fact]
    public async Task A_directory_that_does_not_answer_is_a_503_never_an_empty_sitemap()
    {
        await using var web = PublicSupport.Host(FakeBackend.Throw(new HttpRequestException("down")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await web.NewBrowser().GetAsync("/sitemap.xml")).Status);
    }

    [Fact]
    public async Task Robots_has_the_crawler_rules_and_points_at_the_sitemap()
    {
        await using var web = PublicSupport.Host();
        var page = await web.NewBrowser().GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("text/plain", page.Header("Content-Type"));
        Assert.Equal(["User-Agent: *", "Allow: /", "Disallow: /moje", "Disallow: /panel-agenta", "Disallow: /admin", "Disallow: /dostep", "Disallow: /checkout", "Disallow: /api", "", "Sitemap: http://localhost/sitemap.xml", ""],
            page.Html.Split('\n'));
        Assert.Empty(page.SetCookies);
    }

    [Fact]
    public void The_disallowed_paths_are_the_private_areas_and_the_api()
    {
        // pinned as a literal since the cutover (it was compared with the Next.js list in web/src/lib/seo/sitemap.ts, which went with web/)
        Assert.Equal(["/moje", "/panel-agenta", "/admin", "/dostep", "/checkout", "/api"], Gabinety.Web.Hosting.SeoContent.DisallowedPaths);
    }
}

static class SeoContentProbe
{
    public static string Sitemap(SitemapDto dto) => Gabinety.Web.Hosting.SeoContent.Sitemap("http://localhost", dto);
}
