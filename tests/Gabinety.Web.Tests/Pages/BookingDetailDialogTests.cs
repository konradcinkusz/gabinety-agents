using Gabinety.Web.Api;
using Gabinety.Web.Components.Pages.Bookings;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>UX-031: the header block of each state, the cancel / dispute / no-show actions as native dialogs (enhance.js upgrades them, the markup alone is a visible block), the rebook link.</summary>
public class BookingDetailDialogTests
{
    static readonly string Detail_ = $"/moje/dostepy/{AccessId}";
    static readonly string Client = Detail_ + "?role=client";

    static async Task<ClientAreaFixture> Open(BookingDetailDto access)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Access.Respond = _ => FakeBackend.Json(access);
        return f;
    }

    // ---- dialogs

    [Fact]
    public async Task The_cancel_dialog_is_an_open_alertdialog_that_enhance_js_can_upgrade_with_a_safe_dismiss_link()
    {
        await using var f = await Open(Detail(cancel: true, outcome: new CancelOutcomeDto("full_refund", new MoneyDto(25000, "PLN"))));
        var page = await f.Browser.GetAsync(Client + "&action=cancel");
        var dialog = page.One("dialog.dialog")!;
        Assert.Equal(("alertdialog", "dialog"), (dialog.GetAttribute("role")!, dialog.GetAttribute("data-enhance")!));
        Assert.True(dialog.HasAttribute("open"));
        Assert.Equal("Czy na pewno zrezygnować z pakietu?", page.TextOf("#" + dialog.GetAttribute("aria-labelledby")));
        var dismiss = dialog.QuerySelectorAll("a[data-dialog-dismiss]").Single();
        Assert.Equal(("Nie rezygnuj", Client), (dismiss.TextContent, dismiss.GetAttribute("href")!)); // the URL without ?action=
        Assert.DoesNotContain("action=", dismiss.GetAttribute("href")!);
        Assert.Equal("Tak, zrezygnuj", dialog.QuerySelector("button.btn--danger")!.TextContent);
        Assert.NotNull(dialog.QuerySelector("textarea[name='Model.Reason']"));
    }

    [Fact]
    public async Task The_cancel_dialog_says_the_consequence_once_in_a_callout_danger_when_nothing_comes_back()
    {
        await using var none = await Open(Detail(cancel: true, outcome: new CancelOutcomeDto("no_refund", new MoneyDto(0, "PLN"))));
        var late = (await none.Browser.GetAsync(Client + "&action=cancel")).One("dialog")!;
        var callout = late.QuerySelectorAll(".callout").Single();
        Assert.Contains("callout--danger", callout.ClassName);
        Assert.Equal("Nie otrzymasz zwrotu.", callout.TextContent);
        Assert.DoesNotContain("Do pierwszego użycia rezygnacja", late.TextContent);

        await using var full = await Open(Detail(cancel: true, outcome: new CancelOutcomeDto("full_refund", new MoneyDto(25000, "PLN"))));
        var ok = (await full.Browser.GetAsync(Client + "&action=cancel")).One("dialog .callout")!;
        Assert.Contains("callout--info", ok.ClassName);
        Assert.Equal($"Otrzymasz zwrot {Gabinety.Web.Components.Shared.Money.Pln(25000)}.", ok.TextContent);
    }

    [Fact]
    public async Task When_the_outcome_cannot_be_computed_the_general_rule_is_the_one_sentence()
    {
        await using var f = await Open(Detail(cancel: true, outcome: null));
        var callout = (await f.Browser.GetAsync(Client + "&action=cancel")).One("dialog .callout")!;
        Assert.StartsWith("Do pierwszego użycia rezygnacja oznacza pełny zwrot ceny.", callout.TextContent);
    }

    [Fact]
    public async Task The_dispute_dialog_is_an_alertdialog_named_by_its_heading_with_a_Zamknij_link_back_to_the_detail()
    {
        await using var f = await Open(Detail("Completed", dispute: true));
        var page = await f.Browser.GetAsync(Client + "&action=dispute");
        var dialog = page.One("dialog.dialog")!;
        Assert.Equal(("alertdialog", "dialog"), (dialog.GetAttribute("role")!, dialog.GetAttribute("data-enhance")!));
        Assert.True(dialog.HasAttribute("open"));
        Assert.Equal("Zgłoszenie reklamacji", page.TextOf("#" + dialog.GetAttribute("aria-labelledby")));
        var dismiss = dialog.QuerySelectorAll("a[data-dialog-dismiss]").Single();
        Assert.Equal(("Zamknij", Client), (dismiss.TextContent, dismiss.GetAttribute("href")!));
        Assert.Equal(["Wyślij zgłoszenie"], dialog.QuerySelectorAll("button").Select(b => b.TextContent));
        Assert.Equal("Opis reklamacji", dialog.QuerySelector("label[for=text]")!.TextContent);
    }

    [Fact]
    public async Task Without_the_query_state_there_is_no_dialog_and_the_triggers_are_buttons()
    {
        await using var f = await Open(Detail(cancel: true, dispute: true));
        var page = await f.Browser.GetAsync(Client);
        Assert.Empty(page.All("dialog"));
        Assert.Equal(["Zgłoś reklamację", "Zrezygnuj"], page.All("main form[method=get] button").Select(b => b.TextContent));
    }

    // ---- the header block of each state

    [Fact]
    public async Task A_confirmed_package_leads_with_the_end_of_validity_and_has_no_meeting_link_C03()
    {
        await using var f = await Open(Detail(meeting: "https://meet.example/sala", end: new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.Zero)));
        var page = await f.Browser.GetAsync(Client);
        var head = page.One("[data-testid=booking-head]")!;
        Assert.Equal("Dostęp aktywny do 25 grudnia 2026.", head.QuerySelector(".head__when")!.TextContent);
        Assert.Empty(page.All("a[target=_blank]")); // the meeting link of the old model is gone, whatever the service sends
        Assert.DoesNotContain("meet.example", page.Html); Assert.DoesNotContain("Dołącz", page.Text);
        Assert.Equal("Audyt kodu demo", page.One("h2")!.TextContent); // the harness still waits for this h2
    }

    [Fact]
    public async Task A_completed_booking_leads_with_the_review_and_offers_to_book_again()
    {
        await using var f = await Open(Detail("Completed", review: true));
        var page = await f.Browser.GetAsync(Client);
        var review = page.One("[data-testid=booking-head] a.btn--primary")!;
        Assert.Equal(("Wystaw opinię", $"/moje/opinie/{AccessId}"), (review.TextContent, review.GetAttribute("href")!));
        Assert.Single(page.All("a"), a => a.TextContent == "Wystaw opinię");
        var rebook = page.All("a").Single(a => a.TextContent == "Wykup ponownie");
        Assert.Equal(("btn btn--secondary", "/agent/demo-anna-demowska"), (rebook.ClassName, rebook.GetAttribute("href")!));
        Assert.Empty(page.All("a[target=_blank]"));
    }

    [Fact]
    public async Task A_cancelled_booking_states_the_refund_and_offers_to_book_again_but_not_to_a_practitioner()
    {
        await using var f = await Open(Detail("CancelledByClient", payment: "Refunded", refunded: 25000));
        var page = await f.Browser.GetAsync(Client);
        Assert.Equal($"Zwrot za ten dostęp: {Gabinety.Web.Components.Shared.Money.Pln(25000)}.", page.TextOf("[data-testid=booking-head]"));
        Assert.Contains(page.All("a"), a => a.TextContent == "Wykup ponownie");
        Assert.Matches(@"Zwrócono: 250,00", Page.Squash(page.All(".dl dd").Last().TextContent)); // the hook stays in the dd
        var asPractitioner = await f.Browser.GetAsync(Detail_ + "?role=practitioner");
        Assert.DoesNotContain(asPractitioner.All("a"), a => a.TextContent == "Wykup ponownie");
    }

    [Fact]
    public async Task A_late_cancellation_says_there_is_no_refund_in_the_header()
    {
        await using var f = await Open(Detail("LateCancelledByClient"));
        var head = (await f.Browser.GetAsync(Client)).TextOf("[data-testid=booking-head]");
        Assert.Contains("bez zwrotu", head);
        Assert.DoesNotContain("Zwrócono:", head);
    }

    [Fact]
    public async Task Without_a_slug_or_in_a_state_that_is_not_finished_there_is_no_rebook_link()
    {
        var noSlug = Detail("Completed");
        await using var f = await Open(noSlug with { Agent = noSlug.Agent with { Slug = " " } });
        Assert.DoesNotContain((await f.Browser.GetAsync(Client)).All("a"), a => a.TextContent == "Wykup ponownie");
        await using var g = await Open(Detail("Confirmed"));
        Assert.DoesNotContain((await g.Browser.GetAsync(Client)).All("a"), a => a.TextContent == "Wykup ponownie");
    }

    // ---- pure helpers

    [Theory]
    [InlineData("Completed", "demo-slug", "/agent/demo-slug")]
    [InlineData("CancelledByPractitioner", " demo slug ", "/agent/demo%20slug")]
    [InlineData("Confirmed", "demo-slug", null)]
    [InlineData("Completed", "", null)]
    [InlineData("Completed", null, null)]
    public void The_rebook_target_is_the_profile_of_a_finished_booking_only(string status, string? slug, string? expected) =>
        Assert.Equal(expected, BookingFormat.RebookHref(status, slug));

    [Fact]
    public void Only_a_client_who_gets_nothing_back_is_warned()
    {
        var none = new CancelOutcome(0, 25000);
        Assert.True(CancelOutcome.Warns(none, "client"));
        Assert.False(CancelOutcome.Warns(none, "practitioner"));
        Assert.False(CancelOutcome.Warns(new CancelOutcome(25000, 25000), "client"));
        Assert.False(CancelOutcome.Warns(null, "client"));
    }
}
