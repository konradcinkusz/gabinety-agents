using System.Text.Json;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using W = Gabinety.Web.Api;

namespace Gabinety.Web.Tests.AgentAreaTests;

/// <summary>
/// The practitioner area's DTO copies against the real contracts, in both directions: what the services answer must read into the copies, and what the web sends must read into the services' own
/// request types with the same values (a renamed field fails here and not as a blank column or a silently dropped value).
/// </summary>
public class AgentContractCopiesTests
{
    static readonly JsonSerializerOptions Services = new(JsonSerializerDefaults.Web); // the services write PractitionerStatus as its PascalCase name (ADR-0012 section 1)
    static readonly Guid Id = Guid.Parse("018f0000-0000-7000-8000-000000000002");

    static TCopy Read<TCopy>(object real) => JsonSerializer.Deserialize<TCopy>(JsonSerializer.Serialize(real, real.GetType(), Services), W.ApiClient.Json)!;

    static TReal Send<TReal>(object copy) => JsonSerializer.Deserialize<TReal>(JsonSerializer.Serialize(copy, copy.GetType(), W.ApiClient.Json), Services)!;

    [Fact]
    public void Me_and_the_private_profile_read_into_the_copies_with_the_status_as_its_wire_name()
    {
        var me = Read<W.MeDto>(new MeDto("u1", Id, AgentStatus.Submitted, false, true));
        Assert.Equal(("u1", Id, "Submitted", false, true), (me.UserId, me.AgentId, me.AgentStatus, me.IsAdmin, me.IsModerator));
        var p = Read<W.AgentPrivateDto>(new AgentPrivate(Id, "demo-x", AgentStatus.Rejected, "Powód", "Demo X", "Nagłówek", "Opis", "https://x.example.com/", "https://meet.google.com/a", "5260250274", true, true, true, "demo-gh"));
        Assert.Equal(("Rejected", "Powód", "demo-x", "demo-gh", "5260250274"), (p.Status, p.StatusReason, p.Slug, p.GithubLogin, p.Nip));
        Assert.True(p.EmailVerified && p.GithubLinked && p.NipValid);
        Assert.Equal("https://meet.google.com/a", p.MeetingUrlDefault);
        Assert.Equal("https://x.example.com/", p.PortfolioUrl);
        Assert.Equal("Opis", p.Bio);
    }

    [Fact]
    public void Me_carries_the_agents_of_the_account_and_the_list_of_agents_reads_into_the_copies()
    {
        var other = Guid.Parse("018f0000-0000-7000-8000-000000000003");
        var me = Read<W.MeDto>(new MeDto("u1", Id, AgentStatus.Approved, false, false, [new MeAgent(Id, "demo-a", "Demo A", AgentStatus.Approved), new MeAgent(other, "demo-b", "Demo B", AgentStatus.Suspended)]));
        Assert.Equal([("demo-a", "Approved"), ("demo-b", "Suspended")], me.Agents!.Select(a => (a.Slug, a.Status)));
        Assert.Empty(Read<W.MeDto>(new MeDto("u1", null, null, false, false)).Agents ?? []); // an account with none, or an older service: no agents

        var list = Read<W.AgentSummariesDto>(new { items = new[] { new AgentSummary(Id, "demo-a", "Demo A", "Nagłówek", AgentStatus.Rejected, "Powód") } });
        var row = Assert.Single(list.Items);
        Assert.Equal((Id, "demo-a", "Demo A", "Nagłówek", "Rejected", "Powód"), (row.Id, row.Slug, row.DisplayName, row.Headline, row.Status, row.StatusReason));
    }

    [Fact]
    public void An_offer_reads_into_the_copy_and_the_list_is_wrapped_in_items()
    {
        var offer = new PackagePrivate(Id, "Tytuł", "Opis", 60, new MoneyDto(18050), "code-review", ["dotnet"], false, "https://meet.google.com/a");
        var o = Read<W.PackagePrivateDto>(offer);
        Assert.Equal(("Tytuł", 60, 18050L, "PLN", "code-review", false, "https://meet.google.com/a"), (o.Title, o.DurationMinutes, o.Price.AmountMinor, o.Price.Currency, o.ProblemSlug, o.IsActive, o.MeetingUrl));
        Assert.Equal(["dotnet"], o.TechSlugs);
        var list = Read<W.PackagesDto>(new { items = new[] { offer } });
        Assert.Equal(Id, list.Items.Single().Id);
    }

    [Fact]
    public void What_the_web_sends_reads_into_the_services_own_request_types_with_the_same_values()
    {
        var create = Send<CreateAgentRequest>(new W.CreateAgentBody("Demo Jedyny"));
        Assert.Equal("Demo Jedyny", create.DisplayName);
        var update = Send<UpdateAgentRequest>(new W.UpdateAgentBody("Demo", "Nagłówek", "Opis", "https://x.example.com/", "5260250274"));
        Assert.Equal(("Demo", "Nagłówek", "Opis", "https://x.example.com/", null, "5260250274"), (update.DisplayName, update.Headline, update.Bio, update.PortfolioUrl, update.MeetingUrlDefault, update.Nip)); // no meeting link is sent
        var offer = Send<PackageRequest>(new W.PackageBody("Tytuł oferty", "Opis", 60, 18050, "code-review", ["dotnet", "react"], "Chat", 500));
        Assert.Equal(("Tytuł oferty", "Opis", 60, 18050L, "code-review", null, PackageKind.Chat, 500), (offer.Title, offer.Description, offer.DurationMinutes, offer.PriceMinor, offer.ProblemSlug, offer.MeetingUrl, offer.Kind, offer.Units)); // the kind and the units reach the service
        Assert.Equal(["dotnet", "react"], offer.TechSlugs);
    }

    [Fact]
    public void An_empty_optional_field_is_omitted_from_the_json_not_sent_as_null()
    {
        var json = JsonSerializer.Serialize(new W.UpdateAgentBody("Demo", "H", "B", null, null), W.ApiClient.Json);
        Assert.DoesNotContain("portfolioUrl", json);
        Assert.DoesNotContain("null", json);
    }
}
