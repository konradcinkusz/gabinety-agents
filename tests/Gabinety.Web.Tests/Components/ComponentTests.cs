using Bunit;
using Bunit.TestDoubles;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Layout;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Components.Pages.Practitioners;
using Gabinety.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>Markup, test hooks, state transitions and error rendering with bUnit; the typed clients are wired to a fake handler (BLAZOR-WEB-APP section 13). The roots (render mode) are not visible here: RenderModeTests reads them from disk.</summary>
public class PresentationalComponentTests : BunitContext
{
    [Fact]
    public void A_card_shows_the_practitioner_with_the_hooks_the_journey_uses()
    {
        var cut = Render<AgentCard>(p => p.Add(c => c.Agent, Sample.Card(badges: ["email", "approved"], rating: new RatingDto(4.5, 12), isNew: false)));
        Assert.Equal("practitioner-card", cut.Find("article").GetAttribute("data-testid"));
        var link = cut.Find("h2 a");
        Assert.Equal(("Anna Demowska", "/agent/demo-anna-demowska"), (link.TextContent, link.GetAttribute("href")));
        Assert.Equal("Audyty kodu .NET i konsultacje architektury", cut.Find("p").TextContent);
        Assert.Equal(["Agent AI", "Zweryfikowany"], cut.FindAll(".badge").Select(b => b.TextContent)); // P-LIST-2: one badge, the checks are on the profile
        Assert.Contains("badge--success", cut.Find(".badge--success").ClassName);
        Assert.Equal("\u2605 4,5 (12)", cut.Find(".practitioner-card__rating").TextContent); // the rating is text, not a second badge
        Assert.Equal("Ocena 4,5 (12)", cut.Find(".practitioner-card__rating").GetAttribute("aria-label"));
        Assert.Equal("od 250\u00A0zł", cut.Find(".practitioner-card__from").TextContent); // whole amounts: no grosz
        Assert.Equal("30 min, 60 min", cut.Find(".practitioner-card__durations").TextContent);
        Assert.Equal("card card--link practitioner-card", cut.Find("article").ClassName); // the whole card is the link target
    }

    [Fact]
    public void A_new_practitioner_is_marked_new_and_a_rating_below_the_threshold_is_not_shown()
    {
        var cut = Render<AgentCard>(p => p.Add(c => c.Agent, Sample.Card(badges: [], rating: new RatingDto(5.0, 2), isNew: true)));
        Assert.Equal(["Agent AI", "Nowy"], cut.FindAll(".badge").Select(b => b.TextContent));
    }

    [Fact]
    public void Text_from_the_directory_is_text_not_markup()
    {
        var cut = Render<AgentCard>(p => p.Add(c => c.Agent, Sample.Card(name: "<b>Pogrubiony</b>", headline: "<img src=x onerror=alert(1)>")));
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<b>Pogrubiony</b>", cut.Find("h2 a").TextContent);
    }

    [Theory]
    [InlineData(AlertVariant.Danger, "alert", "callout--danger")]
    [InlineData(AlertVariant.Warning, "status", "callout--warning")]
    [InlineData(AlertVariant.Info, "status", "callout--info")]
    [InlineData(AlertVariant.Success, "status", "callout--success")]
    public void An_alert_announces_itself_only_when_it_is_an_error(AlertVariant variant, string role, string cssClass)
    {
        var cut = Render<Alert>(p => p.Add(a => a.Variant, variant).Add(a => a.TestId, "x").AddChildContent("Tekst"));
        var div = cut.Find("div");
        Assert.Equal(role, div.GetAttribute("role"));
        Assert.Contains(cssClass, div.ClassName);
        Assert.Equal("x", div.GetAttribute("data-testid"));
    }

    [Fact]
    public void The_filters_mark_the_current_choice_and_carry_the_other_filters_in_the_search_form()
    {
        var state = new ListState("audyt-kodu", Duration: 60, MaxPriceMinor: 30000, Q: "kod");
        var view = new ListView(state, Sample.Taxonomy, Sample.Sitemap(5, true, "/agenci/audyt-kodu"));
        var cut = Render<FilterNav>(p => p.Add(f => f.State, state).Add(f => f.Groups, view.Filters()));
        Assert.Equal("Filtry", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(["Problem", "Technologia", "Typowy czas wykonania", "Cena do"], cut.FindAll("h2").Select(h => h.TextContent));
        var current = cut.FindAll("a[aria-current=true]").Select(a => a.TextContent).ToList();
        Assert.Equal(["Audyt kodu", "Wszystkie", "60 min", "do 300,00 zł"], current);
        var form = cut.Find("form[role=search]");
        Assert.Equal(("get", "/agenci/audyt-kodu"), (form.GetAttribute("method"), form.GetAttribute("action")));
        Assert.Equal("60", form.QuerySelector("input[name=duration]")!.GetAttribute("value"));
        Assert.Equal("30000", form.QuerySelector("input[name=maxPriceMinor]")!.GetAttribute("value"));
        Assert.Equal("kod", form.QuerySelector("input[name=q]")!.GetAttribute("value"));
        Assert.Equal("Szukaj po imieniu lub nagłówku", form.QuerySelector("input[name=q]")!.GetAttribute("aria-label")); // an unlabelled control is a violation (SPEC 5 rule 14)
    }

    [Fact]
    public void Pagination_renders_only_the_links_it_has()
    {
        Assert.Empty(Render<Pagination>().FindAll("nav"));
        var next = Render<Pagination>(p => p.Add(x => x.NextHref, "/agenci?cursor=B"));
        Assert.Equal(["/agenci?cursor=B"], next.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Equal("next", next.Find("a").GetAttribute("rel"));
        var both = Render<Pagination>(p => p.Add(x => x.NextHref, "/n").Add(x => x.PreviousHref, "/p"));
        Assert.Equal(["prev", "next"], both.FindAll("a").Select(a => a.GetAttribute("rel")));
        Assert.Equal("Paginacja", both.Find("nav").GetAttribute("aria-label"));
    }
}

public class LayoutTests : BunitContext
{
    /// <summary>The header and the footer ask the Directory whether a signed-in account is a practitioner; the answer is a fake (a client with no profile unless a test says otherwise).</summary>
    readonly FakeBackend _me = new();

    sealed class Token : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>("tok"); }

    public LayoutTests()
    {
        Services.AddSingleton<TimeProvider>(TestClock.Default());
        _me.Respond = _ => FakeBackend.Json(new MeDto("demo-client", null, null, false, false));
        Services.AddSingleton(new MeClient(new HttpClient(_me) { BaseAddress = new Uri("http://directory.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token()));
    }

    static RenderFragment Body(string html = "<h1>Treść</h1>") => b => b.AddMarkupContent(0, html);

    sealed class Thrower : ComponentBase { protected override void OnInitialized() => throw new InvalidOperationException("SECRET-USER-TEXT-IN-AN-EXCEPTION"); }

    [Fact]
    public void The_shell_has_a_skip_link_first_and_the_landmarks()
    {
        AddAuthorization();
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, Body()));
        var first = cut.Find("body, div, a"); // the first element rendered is the skip link
        Assert.Equal("a", first.TagName.ToLowerInvariant());
        Assert.Equal(("#main", "Przejdź do treści", "skip-link"), (first.GetAttribute("href"), first.TextContent, first.ClassName));
        Assert.Equal("Menu główne", cut.Find("header nav").GetAttribute("aria-label"));
        var main = cut.Find("main#main");
        Assert.Equal("-1", main.GetAttribute("tabindex")); // the skip link's target can take focus
        Assert.Single(cut.FindAll("main"));
        Assert.Single(cut.FindAll("footer"));
        Assert.Single(cut.FindAll("header"));
        Assert.Equal("Treść", main.QuerySelector("h1")!.TextContent);
        Assert.Equal("© 2026 Gabinety Tech", cut.Find("footer p.site-footer__copy").TextContent);
        Assert.Equal("Gabinety Tech", cut.Find("a.brand").TextContent.Trim()); // the mark is an svg without text: the name is the link text
    }

    [Fact]
    public void The_navigation_order_is_pinned_for_a_visitor_and_for_a_member()
    {
        var auth = AddAuthorization();
        auth.SetNotAuthorized();
        var visitor = Render<MainLayout>(p => p.Add(l => l.Body, Body()));
        Assert.Equal(["/agenci", "/jak-to-dziala", "/cennik"], visitor.FindAll("header .site-nav a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["/logowanie", "/rejestracja"], visitor.FindAll("header .site-account a").Select(a => a.GetAttribute("href")));
        auth.SetAuthorized("demo-client@demo.example");
        var member = Render<MainLayout>(p => p.Add(l => l.Body, Body()));
        Assert.Equal(["/agenci", "/jak-to-dziala", "/cennik"], member.FindAll("header .site-nav a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["/moje/dostepy", "/moje/klucze", "/konto", "/panel-agenta", "/wyloguj"], member.FindAll("header .account-menu a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void The_flash_area_is_a_polite_live_region_that_shows_known_messages_only()
    {
        AddAuthorization();
        Assert.Equal("polite", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").GetAttribute("aria-live"));
        Assert.Equal("", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/?flash=signed-out");
        Assert.Equal("Wylogowano.", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);
        nav.NavigateTo("/?flash=%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        Assert.Equal("", Render<MainLayout>(p => p.Add(l => l.Body, Body())).Find("[data-testid=flash]").TextContent);
    }

    [Fact]
    public void A_component_that_throws_is_contained_by_the_error_boundary_which_says_so_and_shows_nothing_of_the_exception()
    {
        AddAuthorization();
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, b => { b.OpenComponent<Thrower>(0); b.CloseComponent(); }));
        var alert = cut.Find("[data-testid=error-boundary]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal("Coś poszło nie tak", alert.QuerySelector("h2")!.TextContent);
        Assert.DoesNotContain("SECRET-USER-TEXT", cut.Markup);
        Assert.NotNull(cut.Find("header")); // the shell around it still renders
    }
}

public class MyBookingsTests : BunitContext
{
    readonly FakeBackend _booking = new();
    readonly FakeBackend _me = new();
    readonly BunitAuthorizationContext _auth;
    // The bookings of the fixture start on 1 December 2026 (Sample.Booking): with this clock they are upcoming, nine days ahead.
    readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));

    sealed class Token(string? value) : IAccessTokenSource { public Task<string?> AccessTokenAsync(CancellationToken ct = default) => Task.FromResult(value); }

    public MyBookingsTests()
    {
        _auth = AddAuthorization();
        _auth.SetAuthorized("demo-client@demo.example");
        var http = new HttpClient(_booking) { BaseAddress = new Uri("http://booking.test") };
        Services.AddSingleton(new BookingClient(http, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok")));
        _me.Respond = _ => FakeBackend.Json(new MeDto("demo-client", Guid.NewGuid(), "Approved", false, false)); // a practitioner too: the role tabs show
        Services.AddSingleton(new MeClient(new HttpClient(_me) { BaseAddress = new Uri("http://directory.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok")));
        Services.AddSingleton<TimeProvider>(_clock);
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Access("Pierwsza"), Sample.Access("Druga", "PendingPayment")], null));
    }

    // A SupplyParameterFromQuery parameter takes its value from the URL, as in the browser: navigate, then render.
    IRenderedComponent<MyBookings> Open(string? role = null, string? status = null, string? cursor = null)
    {
        var query = string.Join('&', new[] { role is null ? null : $"role={Uri.EscapeDataString(role)}", status is null ? null : $"status={Uri.EscapeDataString(status)}", cursor is null ? null : $"cursor={Uri.EscapeDataString(cursor)}" }.Where(q => q is not null));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/moje/dostepy" + (query.Length > 0 ? "?" + query : ""));
        return Render<MyBookings>();
    }

    [Fact]
    public void It_lists_the_bookings_with_their_hooks_and_loads_the_client_view_by_default()
    {
        var cut = Open();
        Assert.Equal("Moje dostępy", cut.Find("h1").TextContent);
        Assert.Equal(["Pierwsza", "Druga"], cut.FindAll("[data-testid=booking-row] .booking-row__title").Select(h => h.TextContent)); // the one that expires first
        Assert.Equal(["Aktywny", "Oczekuje na płatność"], cut.FindAll("[data-testid=booking-row] .badge:not([data-testid=ai-badge])").Select(b => b.TextContent));
        Assert.Equal(["badge badge--info", "badge badge--warning"], cut.FindAll("[data-testid=booking-row] .badge:not([data-testid=ai-badge])").Select(b => b.ClassName)); // G-8: the kind follows the state
        Assert.Equal("/api/v1/me/bookings?role=client", _booking.Requests.Single().PathAndQuery);
        Assert.Empty(cut.FindAll("[role=alert]"));
        Assert.Empty(cut.FindAll("a[rel=next]"));
    }

    [Fact]
    public void The_role_is_a_tab_with_the_current_one_marked_not_a_button_pair_G2()
    {
        var cut = Open();
        var nav = cut.Find("nav.tabs");
        Assert.Equal("Dostępy jako", nav.GetAttribute("aria-label"));
        var links = nav.QuerySelectorAll("a.tabs__link").ToList();
        Assert.Equal(["Klient", "Dostawca"], links.Select(a => a.TextContent));
        Assert.Equal(["page", null], links.Select(a => a.GetAttribute("aria-current")));
        Assert.Equal(["/moje/dostepy?role=client", "/moje/dostepy?role=practitioner"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal(["Dostawca"], Open("practitioner").FindAll("a.tabs__link[aria-current=page]").Select(a => a.TextContent));
    }

    [Fact]
    public void A_row_names_the_agent_with_the_badge_for_a_client_and_the_client_for_a_provider_F03()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Access("Z imieniem"), Sample.Access("Bez imienia", practitioner: "")], null));
        var cut = Open();
        var lines = cut.FindAll("[data-testid=booking-party]").Select(p => Page.Squash(p.TextContent)).ToList();
        Assert.Equal("Anna DemowskaAgent AI", lines[0]);
        Assert.Equal("Agent AI", lines[1]); // no name: just the badge, never a stray separator
        Assert.Equal("demo-client", Page.Squash(Open("practitioner").Find("[data-testid=booking-party]").TextContent)); // the other party, for a provider, is the client
    }

    [Fact]
    public void A_row_shows_the_usage_and_the_validity_of_the_package_C03()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Access("Pakiet") with { UnitsTotal = 10, UnitsUsed = 3 }, Sample.Access("Czat", "Confirmed") with { Kind = "Chat", UnitsTotal = 500000, UnitsUsed = 120000 }], null));
        var rows = Open().FindAll("[data-testid=booking-row]");
        Assert.Equal("wykorzystano 3 z 10", rows[0].QuerySelector(".usage__text")!.TextContent);
        Assert.Equal("wykorzystano 120 z 500 tys. tokenów", rows[1].QuerySelector(".usage__text")!.TextContent);
        Assert.Equal("3", rows[0].QuerySelector("progress")!.GetAttribute("value"));
        Assert.Equal("ważny do 1 grudnia 2026", rows[0].QuerySelector("[data-testid=booking-valid-until]")!.TextContent);
    }

    [Fact]
    public void A_row_has_a_details_link_that_keeps_the_role_and_keeps_its_name_for_the_journey()
    {
        var cut = Open("practitioner");
        var link = cut.Find("[data-testid=booking-row] a.booking-row__link");
        Assert.Matches(@"^/moje/dostepy/[0-9a-f-]{36}\?role=practitioner$", link.GetAttribute("href"));
        Assert.Equal("Szczegóły", link.TextContent); // J3 finds it with getByRole("link", { name: "Szczegóły" }) inside the row's listitem: the text is exactly this
        Assert.Equal("Pierwsza", cut.Find($"#{link.GetAttribute("aria-describedby")}").TextContent); // the title says which booking it opens
        Assert.Equal("LI", link.Closest("li")!.TagName);
    }

    [Fact]
    public void The_token_goes_to_the_handler_through_the_request_options_not_through_a_scoped_service()
    {
        // BLAZOR-WEB-APP section 7, trap 2: a message handler lives in its own DI scope and cannot see the scope's services, so the token travels on the request.
        string? seen = "not called";
        var probe = new Probe(r => seen = r.Options.TryGetValue(RequestContext.AccessToken, out var t) ? t : null);
        Services.AddSingleton(new BookingClient(new HttpClient(probe) { BaseAddress = new Uri("http://booking.test") }, new VisitorAddressAccessor(new HttpContextAccessor()), new Token("tok-from-this-scope")));
        Open();
        Assert.Equal("tok-from-this-scope", seen);
    }

    sealed class Probe(Action<HttpRequestMessage> look) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            look(request);
            return Task.FromResult(FakeBackend.Json(new PageDto<BookingViewDto>([], null)));
        }
    }

    [Fact]
    public void An_initial_view_comes_from_the_query_parameters_and_garbage_falls_back()
    {
        Open("practitioner", "Confirmed");
        Assert.Equal("/api/v1/me/bookings?role=practitioner&status=Confirmed", _booking.Requests.Last().PathAndQuery);
        Open("admin", "Hacked");
        Assert.Equal("/api/v1/me/bookings?role=client", _booking.Requests.Last().PathAndQuery);
    }

    [Fact]
    public void The_status_filter_is_a_row_of_chip_links_listing_the_statuses_present_and_marks_the_current_one()
    {
        var cut = Open("practitioner");
        var chips = cut.Find("[data-testid=status-filter]").QuerySelectorAll("a.chip");
        Assert.Empty(cut.FindAll("select")); // no select and no button any more
        Assert.Empty(cut.FindAll("form"));
        Assert.Equal(["Wszystkie", "Oczekuje na płatność", "Aktywny"], chips.Select(a => a.TextContent)); // the statuses the loaded role has, in the contract's order
        Assert.Equal([ "true", null, null], chips.Select(a => a.GetAttribute("aria-current")));
        Assert.Equal("/moje/dostepy?role=practitioner&status=Confirmed", chips[2].GetAttribute("href"));
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void A_status_that_matched_nothing_still_lists_itself_so_the_filter_can_be_left()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([], null));
        var cut = Open("practitioner", "Completed");
        var chips = cut.Find("[data-testid=status-filter]").QuerySelectorAll("a.chip");
        Assert.Equal(["Wszystkie", "Zakończony"], chips.Select(a => a.TextContent));
        Assert.Equal(["/moje/dostepy?role=practitioner", "/moje/dostepy?role=practitioner&status=Completed"], chips.Select(a => a.GetAttribute("href")));
        Assert.Equal([null, "true"], chips.Select(a => a.GetAttribute("aria-current")));
        Assert.Equal("Brak dostępów o tym statusie.", cut.Find("[data-testid=bookings-empty] p").TextContent);
    }

    [Fact]
    public void More_is_a_link_with_the_cursor_that_keeps_the_view_and_the_last_page_has_none()
    {
        _booking.Respond = r => r.RequestUri!.Query.Contains("cursor=C2")
            ? FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Access("Strona druga")], null))
            : FakeBackend.Json(new PageDto<BookingViewDto>([Sample.Access("Strona pierwsza")], "C 2"));
        var cut = Open("practitioner", "Confirmed");
        Assert.Equal("/moje/dostepy?role=practitioner&status=Confirmed&cursor=C%202", cut.Find("a[rel=next]").GetAttribute("href")); // the cursor is escaped
        var second = Open("practitioner", "Confirmed", "C2");
        Assert.Equal("/api/v1/me/bookings?role=practitioner&status=Confirmed&cursor=C2", _booking.Requests.Last().PathAndQuery);
        Assert.Equal(["Strona druga"], second.FindAll("[data-testid=booking-row] .booking-row__title").Select(h => h.TextContent));
        Assert.Empty(second.FindAll("a[rel=next]"));
    }

    [Fact]
    public void An_empty_listing_has_a_heading_a_sentence_and_one_action_and_no_alert()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([], null));
        var empty = Open().Find("[data-testid=bookings-empty]");
        Assert.Equal("Brak dostępów", empty.QuerySelector("h2")!.TextContent);
        Assert.Equal("Znajdź agenta", empty.QuerySelector("a")!.TextContent);
        Assert.Equal("/agenci", empty.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(Open().FindAll("[role=alert]"));
        Assert.Empty(Open().FindAll("ul.bookings")); // never an empty list
    }

    [Fact]
    public void An_empty_filtered_view_offers_to_show_every_status_instead()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([], null));
        var empty = Open("client", "Disputed").Find("[data-testid=bookings-empty]");
        Assert.Equal("Brak dostępów o tym statusie.", empty.QuerySelector("p")!.TextContent);
        Assert.Equal("/moje/dostepy?role=client", empty.QuerySelector("a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData(0, "Brak połączenia z usługą dostępów. Spróbuj ponownie za chwilę.")]
    [InlineData(503, "Dostępy są chwilowo niedostępne. Spróbuj ponownie za chwilę.")]
    [InlineData(429, "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.")]
    [InlineData(500, "Nie udało się wczytać dostępów.")]
    public void A_failure_is_an_alert_in_place_with_a_retry_and_the_page_still_renders(int status, string text)
    {
        _booking.Respond = status == 0 ? FakeBackend.Throw(new HttpRequestException("x")) : _ => FakeBackend.Problem((System.Net.HttpStatusCode)status, "some_code");
        var cut = Open("practitioner", "Confirmed");
        var alert = cut.Find("[data-testid=unavailable]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Equal(text, alert.QuerySelector("p")!.TextContent);
        Assert.Equal("Spróbuj ponownie", alert.QuerySelector("a")!.TextContent);
        Assert.Equal("/moje/dostepy?role=practitioner&status=Confirmed", alert.QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", cut.Markup);
        Assert.Equal("Moje dostępy", cut.Find("h1").TextContent);
        Assert.NotNull(cut.Find("nav.tabs")); // the rest of the page renders
    }

    [Fact]
    public void A_401_says_the_session_ended_with_a_link_back_to_the_same_view_and_not_a_redirect()
    {
        _booking.Respond = _ => FakeBackend.Problem(System.Net.HttpStatusCode.Unauthorized, "unauthenticated");
        var alert = Open("practitioner", "Confirmed").Find("[data-testid=bookings-signed-out]");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("Sesja wygasła", alert.TextContent);
        Assert.Equal("/logowanie?redirect=%2Fmoje%2Fdostepy%3Frole%3Dpractitioner%26status%3DConfirmed", alert.QuerySelector("a")!.GetAttribute("href"));
    }

    // The clock of the fixture is 22 November 2026, 11:00 in Warsaw.
    static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    static BookingViewDto Dated(string title, string status, DateTimeOffset start) =>
        Sample.Access(title, status) with { StartUtc = start, EndUtc = start.AddHours(1) };

    [Fact]
    public void The_list_is_two_sections_split_by_the_end_of_validity_from_the_clock_active_soonest_to_expire_first_and_past_newest_first()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>(
        [
            Dated("Dawna", "Completed", Utc(10, 1, 12)),
            Dated("Dalsza", "Confirmed", Utc(12, 5, 12)),
            Dated("Anulowana", "CancelledByClient", Utc(12, 2, 12)), // in the future, but cancelled: it is not upcoming
            Dated("Niedawna", "Completed", Utc(11, 10, 12)),
            Dated("Przyszła", "Confirmed", Utc(11, 30, 12)),
            Dated("Teraz", "Confirmed", Utc(11, 22, 9)), // its validity ended at the clock: past
        ], null));
        var cut = Open();
        Assert.Equal(["Aktywne", "Zakończone"], cut.FindAll("section h2").Select(h => h.TextContent));
        Assert.Equal(["Przyszła", "Dalsza"], cut.FindAll("[data-testid=bookings-upcoming] .booking-row__title").Select(h => h.TextContent));
        Assert.Equal(["Anulowana", "Teraz", "Niedawna", "Dawna"], cut.FindAll("[data-testid=bookings-past] .booking-row__title").Select(h => h.TextContent));
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void An_empty_section_is_not_shown()
    {
        _booking.Respond = _ => FakeBackend.Json(new PageDto<BookingViewDto>([Dated("Minęła", "Completed", Utc(10, 1, 12))], null));
        var cut = Open();
        Assert.Equal(["Zakończone"], cut.FindAll("section h2").Select(h => h.TextContent));
        Assert.Empty(cut.FindAll("[data-testid=bookings-upcoming]"));
    }

    [Fact]
    public void The_role_tabs_show_only_for_an_account_with_a_practitioner_profile_or_when_the_directory_cannot_say()
    {
        _me.Respond = _ => FakeBackend.Json(new MeDto("demo-client", null, null, false, false));
        var client = Open();
        Assert.Empty(client.FindAll("nav.tabs"));
        Assert.NotEmpty(client.FindAll("[data-testid=status-filter]")); // the filter stays
        Assert.Equal("/api/v1/me", _me.Requests.Last().PathAndQuery);

        _me.Respond = _ => FakeBackend.Problem(System.Net.HttpStatusCode.InternalServerError, "some_code");
        Assert.Single(Open().FindAll("nav.tabs")); // fail soft: the list still works from the URL, so the tabs show

        _me.Respond = _ => FakeBackend.Json(new MeDto("demo-client", Guid.NewGuid(), "Approved", false, false));
        Assert.Single(Open().FindAll("nav.tabs"));
    }
}
