using System.Net;
using System.Text.Json;
using Gabinety.Web.Api;
using static Gabinety.Web.Tests.Pages.ClientAreaFixture;

namespace Gabinety.Web.Tests.Pages;

/// <summary>C-03: the tasks of a package and the API keys, against fake backends. The plain key is shown once; the result of a task is text and never markup.</summary>
public class TasksAndKeysTests
{
    static readonly string Detail_ = $"/moje/dostepy/{BookingId}";
    static readonly string Client = Detail_ + "?role=client";
    static readonly DateTimeOffset At = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    const string Input = "Przygotuj plan migracji dla syntetycznej aplikacji demo, dane testowe.";
    const string PlainKey = "gab_DEMOKEYDEMOKEYDEMOKEYDEMOKEYDEMOKEY0123";
    static readonly Guid KeyId = Guid.Parse("018f0000-0000-7000-8000-0000000000d1");

    static async Task<ClientAreaFixture> OpenDetail(BookingDetailDto booking, IReadOnlyList<AgentTaskDto>? tasks = null, Func<HttpRequestMessage, HttpResponseMessage>? post = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        f.Web.Booking.Respond = r =>
            r.Method == HttpMethod.Post && post is not null ? post(r)
            : r.RequestUri!.AbsolutePath.EndsWith("/tasks") ? FakeBackend.Json(tasks ?? [])
            : FakeBackend.Json(booking);
        return f;
    }

    // ---- tasks

    [Fact]
    public async Task A_task_package_has_the_form_and_the_list_with_state_and_result()
    {
        var done = new AgentTaskDto(Guid.NewGuid(), "succeeded", "Gotowy plan.", At, At.AddMinutes(3));
        var queued = new AgentTaskDto(Guid.NewGuid(), "queued", null, At.AddMinutes(10), null);
        await using var f = await OpenDetail(Detail(unitsTotal: 10, unitsUsed: 2), [done, queued]);
        var page = await f.Browser.GetAsync(Client);
        var section = page.One("section[data-testid=tasks]")!;
        Assert.Equal("Zadania", section.QuerySelector("h2")!.TextContent);
        Assert.Equal("Treść zadania", section.QuerySelector("label[for=task-input]")!.TextContent);
        Assert.Equal(["Zleć zadanie"], section.QuerySelectorAll("form button").Select(b => b.TextContent));
        Assert.Equal(["W kolejce", "Gotowe"], section.QuerySelectorAll("[data-testid=task-status]").Select(s => s.TextContent)); // newest first
        Assert.Equal("Gotowy plan.", section.QuerySelector("[data-testid=task-output]")!.TextContent);
        Assert.Contains(f.Web.Booking.Requests, r => r.PathAndQuery == $"/api/v1/me/bookings/{BookingId}/tasks");
        // ADR-0013, owner decision 3: a pending task does not make the page reload itself (no meta refresh, which axe rejects, WCAG 2.2.1, and which never fired after the enhanced redirect), and the page
        // no longer says it does. What it offers is the link that refreshes the list, which is a plain link to the same page.
        Assert.Empty(page.All("meta[http-equiv=refresh]"));
        Assert.DoesNotContain("odświeża się sam", page.Text);
        var pending = section.QuerySelector("p[role=status]")!;
        Assert.Equal("Zadanie jest w toku. Odśwież listę", Page.Squash(pending.TextContent));
        Assert.StartsWith($"/moje/dostepy/{BookingId}", pending.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task The_result_of_a_task_is_plain_text_never_markup()
    {
        const string Hostile = "<img src=x onerror=alert(1)><script>alert(2)</script> **pogrubione** [link](javascript:alert(3))";
        var done = new AgentTaskDto(Guid.NewGuid(), "succeeded", Hostile, At, At.AddMinutes(1));
        await using var f = await OpenDetail(Detail(), [done]);
        var page = await f.Browser.GetAsync(Client);
        var output = page.One("[data-testid=task-output]")!;
        Assert.Equal(Hostile, output.TextContent); // the characters, as characters
        Assert.Empty(output.QuerySelectorAll("*")); // no element was made out of the text
        Assert.Null(page.One("section[data-testid=tasks] img")); Assert.Null(page.One("section[data-testid=tasks] script")); Assert.Null(page.One("section[data-testid=tasks] a[href^=javascript]"));
        Assert.Empty(page.All("meta[http-equiv=refresh]")); // nothing pending
    }

    [Fact]
    public async Task A_chat_package_has_no_tasks_and_the_service_is_not_asked_for_them()
    {
        await using var f = await OpenDetail(Detail(kind: "Chat", unitsTotal: 500000));
        var page = await f.Browser.GetAsync(Client);
        Assert.Null(page.One("[data-testid=tasks]"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.PathAndQuery.EndsWith("/tasks"));
        Assert.Equal("500 tys. tokenów", page.One("[data-testid=detail-units]")!.TextContent);
    }

    [Fact]
    public async Task An_exhausted_or_inactive_package_offers_no_form()
    {
        await using var exhausted = await OpenDetail(Detail(unitsTotal: 3, unitsUsed: 3));
        var page = await exhausted.Browser.GetAsync(Client);
        Assert.Empty(page.All("section[data-testid=tasks] form"));
        Assert.NotNull(page.One("[data-testid=task-not-active]"));
        await using var cancelled = await OpenDetail(Detail("CancelledByClient"));
        Assert.Empty((await cancelled.Browser.GetAsync(Client)).All("section[data-testid=tasks] form"));
    }

    [Fact]
    public async Task A_failed_read_of_the_tasks_is_a_notice_in_that_region_and_the_page_stays()
    {
        var f = await ClientAreaFixture.SignedIn();
        await using var _ = f;
        f.Web.Booking.Respond = r => r.RequestUri!.AbsolutePath.EndsWith("/tasks") ? FakeBackend.Throw(new HttpRequestException("down"))(r) : FakeBackend.Json(Detail());
        var page = await f.Browser.GetAsync(Client);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Nie udało się wczytać zadań", page.TextOf("[data-testid=tasks-unavailable]"));
        Assert.Equal("Audyt kodu demo", page.One("h2")!.TextContent);
    }

    [Fact]
    public async Task Ordering_a_task_posts_the_trimmed_text_as_a_body_and_redirects_with_a_fixed_key()
    {
        await using var f = await OpenDetail(Detail(), post: _ => FakeBackend.Json(new TaskCreatedDto(Guid.NewGuid(), "queued"), HttpStatusCode.Accepted));
        var page = await f.Post(Client, "task", ("Model.Input", $"  {Input}  "));
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.Equal($"{Detail_}?role=client&done=task", page.Location);
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal($"/api/v1/bookings/{BookingId}/tasks", call.PathAndQuery);
        Assert.Equal(Input, JsonDocument.Parse(call.Body!).RootElement.GetProperty("input").GetString());
        Assert.DoesNotContain("Przygotuj", call.PathAndQuery); // user text is a body only
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains("Przygotuj", StringComparison.Ordinal)); // and never in a log
    }

    [Theory]
    [InlineData("za krótkie", "Treść zadania musi mieć co najmniej 20 znaków.")]
    public async Task A_short_task_is_refused_before_the_service_is_asked(string text, string message)
    {
        await using var f = await OpenDetail(Detail());
        var page = await f.Post(Client, "task", ("Model.Input", text));
        Assert.Equal(message, page.TextOf(".form-summary a"));
        Assert.Equal("za krótkie", page.One("#task-input")!.TextContent); // what was typed stays
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_task_that_is_too_long_is_refused_too()
    {
        await using var f = await OpenDetail(Detail());
        var page = await f.Post(Client, "task", ("Model.Input", new string('a', 20001)));
        Assert.Equal("Treść zadania może mieć najwyżej 20000 znaków.", page.TextOf(".form-summary a"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task An_exhausted_package_says_so_in_our_words_when_the_service_refuses()
    {
        await using var f = await OpenDetail(Detail(), post: _ => FakeBackend.Problem((HttpStatusCode)429, "insufficient_quota"));
        var page = await f.Post(Client, "task", ("Model.Input", Input));
        Assert.Equal("Ten pakiet jest wyczerpany.", page.TextOf("[data-testid=action-error] p"));
        Assert.DoesNotContain("SERVER-TITLE-MUST-NOT-APPEAR", page.Html);
    }

    // ---- API keys

    static async Task<ClientAreaFixture> OpenKeys(IReadOnlyList<ApiKeyDto>? keys = null, Func<HttpRequestMessage, HttpResponseMessage>? write = null)
    {
        var f = await ClientAreaFixture.SignedIn();
        var list = keys ?? [];
        f.Web.Booking.Respond = r => r.Method == HttpMethod.Get ? FakeBackend.Json(list) : write is not null ? write(r) : new HttpResponseMessage(HttpStatusCode.NoContent);
        return f;
    }

    [Fact]
    public async Task The_list_shows_name_prefix_created_and_last_used_and_never_a_key()
    {
        await using var f = await OpenKeys([new ApiKeyDto(KeyId, "Moje IDE", "AbCd1234", At, At.AddDays(2)), new ApiKeyDto(Guid.NewGuid(), "Skrypt", "ZzZz9999", At, null)]);
        var page = await f.Browser.GetAsync("/moje/klucze");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Klucze API", page.TextOf("h1"));
        var rows = page.All("[data-testid=key-row]");
        Assert.Equal(["Moje IDE", "gab_AbCd1234", "20 listopada 2026", "22 listopada 2026"], rows[0].QuerySelectorAll("th, td").Take(4).Select(c => Page.Squash(c.TextContent)));
        Assert.Contains("jeszcze nie użyto", rows[1].TextContent);
        Assert.Null(page.One("[data-testid=new-key]"));
        Assert.DoesNotContain("gab_" + new string('A', 10), page.Html);
        Assert.Equal("/api/v1/me/api-keys", f.Web.Booking.Requests.Single(r => r.PathAndQuery.StartsWith("/api/v1/me/api-keys")).PathAndQuery);
    }

    [Fact]
    public async Task A_visitor_is_sent_to_sign_in()
    {
        await using var f = await ClientAreaFixture.Visitor();
        var page = await f.Browser.GetAsync("/moje/klucze");
        Assert.Equal(HttpStatusCode.Found, page.Status);
        Assert.StartsWith("/logowanie?redirect=%2Fmoje%2Fklucze", page.Location);
        Assert.Empty(f.Web.Booking.Requests);
    }

    [Fact]
    public async Task A_created_key_is_shown_once_with_a_warning_and_a_copy_button_and_is_absent_when_the_page_is_read_again()
    {
        await using var f = await OpenKeys(write: _ => FakeBackend.Json(new ApiKeyCreatedDto(KeyId, "Moje IDE", "DEMOKEYD", PlainKey, At), HttpStatusCode.Created));
        var created = await f.Post("/moje/klucze", "createkey", ("Model.Name", "  Moje IDE  "));
        Assert.Equal(HttpStatusCode.OK, created.Status);
        var box = created.One("[data-testid=new-key]")!;
        Assert.Equal(PlainKey, box.QuerySelector("[data-testid=new-key-value]")!.TextContent);
        Assert.Contains("tylko raz", box.TextContent); Assert.Equal("alert", box.QuerySelector(".callout--warning")!.GetAttribute("role"));
        var copy = box.QuerySelector("button[data-enhance=copy]")!;
        Assert.Equal((PlainKey, "Skopiowano", "Kopiuj klucz"), (copy.GetAttribute("data-copy"), copy.GetAttribute("data-done"), copy.TextContent));
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal(("/api/v1/me/api-keys", "Moje IDE"), (call.PathAndQuery, JsonDocument.Parse(call.Body!).RootElement.GetProperty("name").GetString()));
        Assert.DoesNotContain(f.Logs.Lines, l => l.Contains(PlainKey, StringComparison.Ordinal));

        var again = await f.Browser.GetAsync("/moje/klucze"); // the list read again: the service never sends the key
        Assert.Null(again.One("[data-testid=new-key]"));
        Assert.DoesNotContain(PlainKey, again.Html);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")]
    public async Task A_missing_name_is_refused_before_the_service_is_asked(string name)
    {
        await using var f = await OpenKeys();
        var page = await f.Post("/moje/klucze", "createkey", ("Model.Name", name));
        Assert.Equal("Podaj nazwę klucza, najwyżej 60 znaków.", page.TextOf(".form-summary a"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task The_sixth_key_is_refused_by_the_service_and_the_page_says_why()
    {
        await using var f = await OpenKeys(write: _ => FakeBackend.Problem((HttpStatusCode)422, "validation_failed", new[] { "name" }));
        var page = await f.Post("/moje/klucze", "createkey", ("Model.Name", "Szósty"));
        Assert.Contains("5 aktywnych kluczy", page.Text);
        Assert.Null(page.One("[data-testid=new-key]"));
    }

    [Fact]
    public async Task Revoking_asks_first_then_deletes_the_key_and_redirects_with_a_fixed_key()
    {
        await using var f = await OpenKeys([new ApiKeyDto(KeyId, "Moje IDE", "AbCd1234", At, null)]);
        var ask = await f.Browser.GetAsync($"/moje/klucze?revoke={KeyId}");
        var dialog = ask.One("dialog[data-testid=revoke-dialog]")!;
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Contains("„Moje IDE”", dialog.TextContent);
        Assert.Equal("/moje/klucze", dialog.QuerySelector("a[data-dialog-dismiss]")!.GetAttribute("href"));
        Assert.DoesNotContain(f.Web.Booking.Requests, r => r.Method == HttpMethod.Delete); // a GET changes nothing

        var page = await f.Post($"/moje/klucze?revoke={KeyId}", "revokekey");
        Assert.Equal("/moje/klucze?done=revoked", page.Location);
        var call = f.Web.Booking.Requests.Single(r => r.Method == HttpMethod.Delete);
        Assert.Equal($"/api/v1/me/api-keys/{KeyId}", call.PathAndQuery);
        Assert.Equal("Klucz został unieważniony.", (await f.Browser.GetAsync("/moje/klucze?done=revoked")).One("[data-testid=done]")!.TextContent);
    }

    [Fact]
    public async Task The_account_menu_links_to_the_keys()
    {
        await using var f = await OpenKeys();
        var page = await f.Browser.GetAsync("/moje/klucze");
        Assert.Contains(page.All(".account-menu a"), a => a.TextContent == "Klucze API" && a.GetAttribute("href") == "/moje/klucze");
    }
}
