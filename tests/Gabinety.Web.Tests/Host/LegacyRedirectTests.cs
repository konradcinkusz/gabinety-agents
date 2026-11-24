using System.Net;
using Gabinety.Web.Hosting;

namespace Gabinety.Web.Tests.Pipeline;

/// <summary>E-02: the old page addresses answer 301 to the new ones, with the rest of the path and the query string kept.</summary>
public class LegacyRedirectTests
{
    static HttpClient Client(WebFactory web) => web.CreateClient(new() { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/programisci", "/agenci")]
    [InlineData("/programisci/audyt-kodu", "/agenci/audyt-kodu")]
    [InlineData("/programisci/audyt-kodu/dotnet", "/agenci/audyt-kodu/dotnet")]
    [InlineData("/gabinet/demo-anna-demowska", "/agent/demo-anna-demowska")]
    [InlineData("/rezerwacja/5f0c1a2e-0000-0000-0000-000000000001", "/dostep/5f0c1a2e-0000-0000-0000-000000000001")]
    [InlineData("/rezerwacja/potwierdzenie/5f0c1a2e-0000-0000-0000-000000000001", "/dostep/potwierdzenie/5f0c1a2e-0000-0000-0000-000000000001")]
    [InlineData("/moje/rezerwacje", "/moje/dostepy")]
    [InlineData("/moje/rezerwacje/5f0c1a2e-0000-0000-0000-000000000001", "/moje/dostepy/5f0c1a2e-0000-0000-0000-000000000001")]
    [InlineData("/gabinet-programisty", "/panel-agenta")]
    [InlineData("/gabinet-programisty/profil", "/panel-agenta/profil")]
    [InlineData("/gabinet-programisty/oferty", "/panel-agenta/oferty")]
    [InlineData("/gabinet-programisty/platnosci", "/panel-agenta/platnosci")]
    [InlineData("/Programisci/Audyt-Kodu", "/agenci/Audyt-Kodu")]
    public void Target_maps_a_legacy_path_and_keeps_the_rest(string old, string expected) =>
        Assert.Equal(expected, LegacyRedirects.Target(new PathString(old)));

    [Theory]
    [InlineData("/")]
    [InlineData("/agenci")]
    [InlineData("/agent/demo-anna")]
    [InlineData("/gabinety")]            // not a whole segment
    [InlineData("/gabinet-programistyx")]
    [InlineData("/moje/dostepy")]
    [InlineData("/moje/opinie")]
    [InlineData("/moje/klucze")]
    [InlineData("/regulamin")]
    public void Target_leaves_current_and_unrelated_paths_alone(string path) => Assert.Null(LegacyRedirects.Target(new PathString(path)));

    [Theory]
    [InlineData("/programisci?q=audyt%20kodu&page=2", "/agenci?q=audyt%20kodu&page=2")]
    [InlineData("/gabinet/demo-anna-demowska?opinie=abc", "/agent/demo-anna-demowska?opinie=abc")]
    [InlineData("/moje/rezerwacje/5f0c1a2e-0000-0000-0000-000000000001?role=client&action=cancel", "/moje/dostepy/5f0c1a2e-0000-0000-0000-000000000001?role=client&action=cancel")]
    [InlineData("/rezerwacja/5f0c1a2e-0000-0000-0000-000000000001?gabinet=demo-anna", "/dostep/5f0c1a2e-0000-0000-0000-000000000001?gabinet=demo-anna")]
    [InlineData("/gabinet-programisty", "/panel-agenta")]
    public async Task The_host_answers_301_with_the_new_address_and_the_query(string old, string expected)
    {
        await using var web = new WebFactory();
        var response = await Client(web).GetAsync(old);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_head_request_is_redirected_too_and_a_post_is_not()
    {
        await using var web = new WebFactory();
        var head = await Client(web).SendAsync(new HttpRequestMessage(HttpMethod.Head, "/programisci?q=a"));
        Assert.Equal((HttpStatusCode.MovedPermanently, "/agenci?q=a"), (head.StatusCode, head.Headers.Location!.OriginalString));
        var post = await Client(web).PostAsync("/programisci", new StringContent(""));
        Assert.NotEqual(HttpStatusCode.MovedPermanently, post.StatusCode);
    }
}
