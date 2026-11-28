using System.Net;
using System.Text.Json.Nodes;
using Gabinety.Web.Api;

namespace Gabinety.Web.Tests.PractitionerAreaTests;

/// <summary>
/// E-04: one provider account with several agents, through the real host. The fake answers like the services do: with two agents a call without <c>?agent=</c> is 422 on <c>agent</c>, an unknown
/// slug is 404, the list is oldest first, a create past the limit is 422 <c>too_many_agents</c>.
/// </summary>
public class MultiAgentHostTests
{
    sealed class Agents
    {
        public readonly List<AgentPrivateDto> List = [];
        public readonly List<AccessViewDto> Accesses = [];
        public int Max = 10;

        public Agents(params string[] slugs) { foreach (var s in slugs) Add(s); }

        public AgentPrivateDto Add(string slug) =>
            Add(new AgentPrivateDto(Guid.NewGuid(), slug, "Draft", null, "Demo " + slug, "", "", null, null, null, true, false, false, null));

        AgentPrivateDto Add(AgentPrivateDto p) { List.Add(p); return p; }

        public void Attach(WebFactory web)
        {
            web.Directory.Respond = Directory;
            web.Access.Respond = Access;
        }

        static HttpResponseMessage Problem(HttpStatusCode status, string code, string? field = null)
        {
            var o = new JsonObject { ["type"] = $"urn:gabinety:error:{code}", ["title"] = "SERVER-TITLE-MUST-NOT-APPEAR", ["status"] = (int)status, ["code"] = code };
            if (field is not null) o["errors"] = new JsonObject { [field] = new JsonArray("SERVER-MESSAGE-MUST-NOT-APPEAR") };
            return new HttpResponseMessage(status) { Content = new StringContent(o.ToJsonString(), System.Text.Encoding.UTF8, "application/problem+json") };
        }

        HttpResponseMessage Directory(HttpRequestMessage r)
        {
            var path = r.RequestUri!.AbsolutePath;
            var agent = System.Web.HttpUtility.ParseQueryString(r.RequestUri.Query)["agent"];
            const string me = "/api/v1/me/practitioner";
            if (path == "/api/v1/taxonomy") return FakeBackend.Json(new TaxonomyDto("1", [new CategoryDto("code-review", "Przegląd kodu")], [new CategoryDto("dotnet", ".NET")]));
            if (path == "/api/v1/me/practitioners") return FakeBackend.Json(new { items = List.Select(p => new AgentSummaryDto(p.Id, p.Slug, p.DisplayName, p.Headline, p.Status, p.StatusReason)) });
            if (path == me && r.Method == HttpMethod.Post)
            {
                if (List.Count >= Max) return Problem(HttpStatusCode.UnprocessableEntity, "too_many_agents");
                var name = JsonNode.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!["displayName"]!.GetValue<string>().Trim();
                return FakeBackend.Json(Add("demo-" + name.ToLowerInvariant().Replace(' ', '-')), HttpStatusCode.Created);
            }
            if (!path.StartsWith(me)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (List.Count == 0) return Problem(HttpStatusCode.NotFound, "not_found");
            if (agent is null && List.Count > 1) return Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", "agent");
            var p = agent is null ? List[0] : List.SingleOrDefault(x => x.Slug == agent);
            if (p is null) return Problem(HttpStatusCode.NotFound, "not_found");
            if (path == me) return FakeBackend.Json(p);
            if (path == me + "/offerings") return FakeBackend.Json(new { items = Array.Empty<PackagePrivateDto>() });
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        HttpResponseMessage Access(HttpRequestMessage r)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/api/v1/me/bookings") return FakeBackend.Json(new PageDto<AccessViewDto>(Accesses, null));
            if (path == "/api/v1/me/payments/account") return FakeBackend.Json(new PaymentAccountDto("Stripe", "none"));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public static AccessViewDto Bought(string slug, string title) =>
            new(Guid.NewGuid(), "Confirmed", DateTimeOffset.UtcNow.AddDays(-1), new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), 30, title, new MoneyDto(10000, "PLN"),
                new AgentRefDto(Guid.NewGuid(), "Demo " + slug, slug), new ClientRefDto("Demo Klient"), "Tasks", 10, 1);
    }

    static async Task<(WebFactory Web, Browser Browser, Agents World)> Host(Action<Agents>? setup = null, params string[] slugs)
    {
        var web = new WebFactory();
        var world = new Agents(slugs);
        setup?.Invoke(world);
        world.Attach(web);
        var browser = web.NewBrowser();
        await SignIn.As(web, browser);
        return (web, browser, world);
    }

    static void TwoAccesses(Agents w) { w.Accesses.Add(Agents.Bought("demo-alfa", "Pakiet Alfa")); w.Accesses.Add(Agents.Bought("demo-beta", "Pakiet Beta")); w.Accesses.Add(Agents.Bought("demo-beta", "Pakiet Beta 2")); }

    [Fact]
    public async Task With_two_agents_the_dashboard_lists_them_and_totals_the_purchases_of_all()
    {
        var (web, browser, _) = await Host(TwoAccesses, "demo-alfa", "demo-beta");
        await using var _w = web;
        var page = await browser.GetAsync("/panel-agenta");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Twoi agenci", page.TextOf("h1"));
        var rows = page.All("[data-testid=agent]");
        Assert.Equal(["demo-alfa", "demo-beta"], rows.Select(x => x.GetAttribute("data-slug")));
        Assert.Equal("/panel-agenta?agent=demo-beta", rows[1].QuerySelector("a.btn--primary")!.GetAttribute("href"));
        Assert.Equal("Aktywne pakiety (suma po wszystkich agentach): 3", page.TextOf("[data-testid=active-packages]"));
        Assert.Equal(3, page.All("[data-testid=totals] li.upcoming__row").Count);
        Assert.Equal("/panel-agenta/nowy", page.One("[data-testid=add-agent]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task A_chosen_agent_gets_its_own_dashboard_with_its_own_purchases_and_the_agent_kept_in_the_tabs()
    {
        var (web, browser, _) = await Host(TwoAccesses, "demo-alfa", "demo-beta");
        await using var _w = web;
        var page = await browser.GetAsync("/panel-agenta?agent=demo-beta");

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Panel dostawcy", page.TextOf("h1"));
        Assert.Contains("demo-beta", page.TextOf("[data-testid=agent-bar]"));
        var tabs = page.All("nav.tabs a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(["/panel-agenta?agent=demo-beta", "/panel-agenta/profil?agent=demo-beta", "/panel-agenta/oferty?agent=demo-beta", "/panel-agenta/platnosci?agent=demo-beta"], tabs);
        Assert.Contains(web.Directory.Requests, r => r.PathAndQuery == "/api/v1/me/practitioner?agent=demo-beta");
        Assert.Contains(web.Access.Requests, r => r.PathAndQuery == "/api/v1/me/payments/account?agent=demo-beta");
    }

    [Fact]
    public async Task The_profile_offers_and_payments_pages_work_on_the_chosen_agent_and_ask_the_services_for_it()
    {
        var (web, browser, world) = await Host(null, "demo-alfa", "demo-beta");
        await using var _w = web;
        world.List[1] = world.List[1] with { Headline = "Nagłówek agenta Beta", Bio = "Opis Beta" };

        var profile = await browser.GetAsync("/panel-agenta/profil?agent=demo-beta");
        Assert.Equal(HttpStatusCode.OK, profile.Status);
        Assert.Equal("Nagłówek agenta Beta", profile.One("#headline")!.GetAttribute("value"));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/panel-agenta/oferty?agent=demo-beta")).Status);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/panel-agenta/platnosci?agent=demo-beta")).Status);

        Assert.Contains(web.Directory.Requests, r => r.PathAndQuery == "/api/v1/me/practitioner?agent=demo-beta");
        Assert.Contains(web.Directory.Requests, r => r.PathAndQuery == "/api/v1/me/practitioner/offerings?agent=demo-beta");
        Assert.Contains(web.Access.Requests, r => r.PathAndQuery == "/api/v1/me/payments/account?agent=demo-beta");
        Assert.DoesNotContain(web.Directory.Requests, r => r.PathAndQuery.StartsWith("/api/v1/me/practitioner") && !r.PathAndQuery.Contains("agent=") && r.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("/panel-agenta/profil")] [InlineData("/panel-agenta/oferty")] [InlineData("/panel-agenta/platnosci")]
    public async Task A_page_of_one_agent_opened_without_the_agent_sends_a_provider_with_two_to_the_overview(string path)
    {
        var (web, browser, _) = await Host(null, "demo-alfa", "demo-beta");
        await using var _w = web;
        var page = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/panel-agenta", page.Location);
    }

    [Fact]
    public async Task An_agent_that_is_not_the_accounts_is_sent_to_the_overview_not_shown()
    {
        var (web, browser, _) = await Host(null, "demo-alfa", "demo-beta");
        await using var _w = web;
        var page = await browser.GetAsync("/panel-agenta?agent=demo-cudzy");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/panel-agenta", page.Location);
    }

    [Fact]
    public async Task One_agent_needs_no_agent_in_any_address_or_call()
    {
        var (web, browser, _) = await Host(null, "demo-alfa");
        await using var _w = web;
        var page = await browser.GetAsync("/panel-agenta");

        Assert.Equal("Panel dostawcy", page.TextOf("h1"));
        Assert.Null(page.One("[data-testid=agent-bar]"));
        Assert.All(page.All("nav.tabs a"), a => Assert.DoesNotContain("agent=", a.GetAttribute("href")));
        Assert.All(web.Directory.Requests.Concat(web.Access.Requests), r => Assert.DoesNotContain("agent=", r.PathAndQuery));
    }

    [Fact]
    public async Task Adding_an_agent_posts_a_create_without_the_current_agent_and_lands_in_the_new_agents_panel()
    {
        var (web, browser, world) = await Host(null, "demo-alfa");
        await using var _w = web;
        var form = await browser.GetAsync("/panel-agenta/nowy?agent=demo-alfa");
        Assert.Equal("Dodaj agenta", form.TextOf("h1"));

        var response = await browser.Submit("/panel-agenta/nowy?agent=demo-alfa", "create-agent", [new("displayName", " Nowy Agent ")]);

        Assert.Contains((int)response.Status, new[] { 302, 303 });
        Assert.Equal("/panel-agenta?agent=demo-nowy-agent&flash=gabinet-created", response.Location);
        Assert.Equal(["demo-alfa", "demo-nowy-agent"], world.List.Select(p => p.Slug));
        var post = web.Directory.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("/api/v1/me/practitioner", post.PathAndQuery); // creating is not scoped to an agent
        var after = await browser.GetAsync(response.Location!);
        Assert.Equal("Panel dostawcy", after.TextOf("h1"));
        Assert.Contains("demo-nowy-agent", after.TextOf("[data-testid=agent-bar]"));
    }

    [Fact]
    public async Task Past_the_limit_the_form_says_so_and_no_agent_is_added()
    {
        var (web, browser, world) = await Host(w => w.Max = 2, "demo-alfa", "demo-beta");
        await using var _w = web;
        var response = await browser.Submit("/panel-agenta/nowy", "create-agent", [new("displayName", "Trzeci Agent")]);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Contains("Osiągnięto limit agentów", response.TextOf("[data-testid=create-error]"));
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", response.Html);
        Assert.Equal(2, world.List.Count);
    }

    [Fact]
    public async Task A_name_that_is_too_short_is_refused_before_any_call()
    {
        var (web, browser, _) = await Host(null, "demo-alfa");
        await using var _w = web;
        var response = await browser.Submit("/panel-agenta/nowy", "create-agent", [new("displayName", "a")]);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("true", response.One("#displayName")!.GetAttribute("aria-invalid"));
        Assert.DoesNotContain(web.Directory.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task The_new_agent_page_is_for_signed_in_users_only()
    {
        await using var web = new WebFactory();
        var page = await web.NewBrowser().GetAsync("/panel-agenta/nowy");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=", page.Location);
    }

    [Fact]
    public async Task The_payment_return_address_keeps_the_agent_it_was_made_for()
    {
        var (web, browser, _) = await Host(null, "demo-alfa", "demo-beta");
        await using var _w = web;
        var page = await browser.GetAsync("/practitioner/payments?agent=demo-beta&return=1");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal("/panel-agenta/platnosci?agent=demo-beta&return=1", page.Location);
    }
}
