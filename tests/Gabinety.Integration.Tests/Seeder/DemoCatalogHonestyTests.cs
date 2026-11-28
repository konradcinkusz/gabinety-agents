using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>
/// The catalog's rating baseline (what the Directory shows before Reputation pushes anything) must equal the reviews the seeder really writes: a profile never shows
/// more reviews than exist. The reviews are read from <see cref="ReputationPlan"/>, each on a Completed booking of <see cref="BookingPlan"/>, made with the
/// n-th Approved practitioner (the Booking step orders them by key, which is the catalog order).
/// </summary>
public class DemoCatalogHonestyTests
{
    static readonly DemoPractitioner[] Approved = DemoCatalog.Agents.Where(p => p.Target == DemoStatus.Approved && p.Packages.Length > 0).OrderBy(p => p.UserName, StringComparer.Ordinal).ToArray();

    static (int Count, int Sum) Seeded(DemoPractitioner p)
    {
        var mine = ReputationPlan.All.Where(r =>
        {
            var booking = BookingPlan.All.Single(b => b.Key == r.BookingKey);
            Assert.Equal(BookingTarget.Completed, booking.Target);
            Assert.True(booking.Tasks > 0, $"{booking.Key} is reviewed, so a task must be ordered on it (a review needs UnitsUsed above 0)");
            return Approved[booking.Agent % Approved.Length].Number == p.Number;
        }).ToList();
        return (mine.Count, mine.Sum(r => r.Rating));
    }

    [Fact]
    public void Every_catalog_review_count_and_average_is_what_the_seeder_creates()
    {
        foreach (var p in DemoCatalog.Agents.Where(p => p.Stats is not null))
        {
            var (count, sum) = Seeded(p);
            Assert.True(p.Stats!.Reviews == count, $"{p.Slug}: the catalog says {p.Stats.Reviews} reviews, the seeder creates {count}");
            Assert.Equal(count == 0 ? 0m : (decimal)sum / count, p.Stats.Average);
        }
    }

    [Fact]
    public void Every_seeded_review_lands_on_an_approved_practitioner_so_the_check_above_sees_all_of_them()
    {
        var total = DemoCatalog.Agents.Where(p => p.Stats is not null).Sum(p => Seeded(p).Count);
        Assert.Equal(ReputationPlan.All.Length, total);
    }

    [Fact]
    public void The_suspended_practitioner_claims_no_reviews_because_the_seeder_makes_no_booking_for_them()
    {
        var suspended = DemoCatalog.Agents.Single(p => p.Target == DemoStatus.Suspended);
        Assert.Equal(0, suspended.Stats!.Reviews);
    }

    [Fact]
    public void Journey_3_starts_from_an_approved_practitioner_with_no_review()
    {
        Assert.Contains(Approved, p => p.Stats!.Reviews == 0 && Seeded(p).Count == 0);
        Assert.Equal(0, Approved.Single(p => p.Slug == "demo-celina-przykladowska").Stats!.Reviews);
    }

    [Fact]
    public void Every_approved_agent_sells_task_packages_and_exactly_one_Chat_package_of_500_thousand_tokens_for_99_zloty()
    {
        Assert.Equal(6, Approved.Length);
        foreach (var p in Approved)
        {
            var tasks = p.Packages.Where(o => o.Kind == DemoKind.Tasks).ToList();
            Assert.InRange(tasks.Count, 1, 2);
            Assert.All(tasks, o => Assert.Contains(o.Units, new[] { 1, 5 }));
            var chat = Assert.Single(p.Packages, o => o.Kind == DemoKind.Chat);
            Assert.Equal((500, 9900L), (chat.Units, chat.PriceMinor));
        }
    }

    [Fact]
    public void Texts_are_in_the_first_person_of_the_agent_and_carry_no_contact_detail_or_long_digit_run()
    {
        foreach (var p in DemoCatalog.Agents)
        {
            var texts = new[] { p.Headline, p.Bio }.Concat(p.Packages.SelectMany(o => new[] { o.Title, o.Description }));
            Assert.All(texts, t => Assert.DoesNotMatch(@"@|https?://|www\.|\d{5,}", t));
            if (p.Target is DemoStatus.Approved or DemoStatus.Submitted) Assert.StartsWith("Jestem agentem AI", p.Bio);
            if (p.Packages.Length > 0) Assert.InRange(p.Bio.Length, 200, 2000);
        }
    }

    [Fact]
    public void Every_booking_of_the_plan_buys_a_package_the_agent_really_sells_and_orders_tasks_only_on_task_packages()
    {
        foreach (var b in BookingPlan.All)
        {
            var agent = Approved[b.Agent % Approved.Length];
            var offering = BookingSeedStep.Pick(new SeededPractitioner(agent.UserName, "u", Guid.Empty, agent.Slug), b.Package);
            Assert.Contains(offering, agent.Packages);
            if (b.Package == PackagePick.Chat) Assert.Equal(0, b.Tasks);
            else Assert.InRange(b.Tasks, 0, offering.Units);
        }
    }
}
