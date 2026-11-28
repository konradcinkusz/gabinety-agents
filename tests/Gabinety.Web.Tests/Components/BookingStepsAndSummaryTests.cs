using Bunit;
using Gabinety.Web.Components.Pages.Booking;
using Gabinety.Web.Components.Pages.Bookings;
using Gabinety.Web.Tests.Pages;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Rendering;

/// <summary>C-02: the step list and the package summary.</summary>
public class BookingStepsTests : BunitContext
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void One_step_is_current_the_earlier_ones_are_marked_done_for_assistive_technology(int current)
    {
        var cut = Render<BookingSteps>(p => p.Add(c => c.Current, current));
        Assert.Equal("Kroki wykupu", cut.Find("nav").GetAttribute("aria-label"));
        var items = cut.FindAll("nav > ol > li");
        Assert.Equal(["Pakiet", "Płatność", "Gotowe"], items.Select(li => li.QuerySelector(".booking-steps__name")!.FirstChild!.TextContent));
        var currentItems = cut.FindAll("[aria-current]");
        Assert.Equal("step", Assert.Single(currentItems).GetAttribute("aria-current"));
        Assert.Equal(items[current].InnerHtml, currentItems[0].InnerHtml);
        var done = items.Where(li => li.TextContent.Contains("(ukończony)")).ToList();
        Assert.Equal(current, done.Count);
        Assert.All(done, li => Assert.NotNull(li.QuerySelector(".sr-only")));
        Assert.All(items.Take(current), li => Assert.Contains("booking-steps__item--done", li.ClassName));
    }

    [Fact]
    public void The_form_is_on_the_first_step_called_Pakiet() =>
        Assert.Contains("Pakiet", Render<BookingSteps>(p => p.Add(c => c.Current, 0)).Find("[aria-current=step]").TextContent);

    [Fact]
    public void The_summary_has_no_time_and_no_change_link_only_agent_units_validity_and_price()
    {
        var cut = Render<ConsultationSummary>(p => p.Add(c => c.Package, "Audyt").Add(c => c.Units, "10 zadań, typowy czas ok. 30 min").Add(c => c.Validity, "30 dni od zakupu").Add(c => c.PriceMinor, 100));
        Assert.Equal(["Pakiet", "Zawartość", "Ważność", "Do zapłaty"], cut.FindAll("dt").Select(d => d.TextContent));
        Assert.Empty(cut.FindAll("a"));
    }
}

public class BookingFormUx020Tests
{
    [Fact]
    public async Task The_form_shows_the_first_step_the_hold_notice_and_the_refund_rule_and_no_time()
    {
        await using var f = await ClientAreaFixture.SignedIn();
        var page = await f.Browser.GetAsync(AccessUrl);
        Assert.Equal("Pakiet", page.One("nav[aria-label='Kroki wykupu'] [aria-current=step] .booking-steps__name")!.TextContent.Trim());
        Assert.Empty(page.All(".booking-summary a"));
        var notice = page.One(".booking-summary .callout--info")!.TextContent;
        Assert.Contains("Zakup jest wstrzymany na 10 minut", notice);
        Assert.Contains("Do pierwszego użycia możesz zrezygnować z pełnym zwrotem.", notice);
    }
}
