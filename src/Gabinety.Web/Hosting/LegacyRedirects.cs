namespace Gabinety.Web.Hosting;

/// <summary>
/// E-02: the page addresses of the consultation product moved to the agent marketplace. The old addresses answer 301 to the new ones and keep the rest of the
/// path and the query string, so bookmarks, search results and old e-mails still work. Only <c>GET</c> and <c>HEAD</c> are redirected (a browser follows
/// them with <c>GET</c>); the match is on whole path segments, case-insensitive, and runs before authentication so a signed-out visitor is redirected, not sent to sign in.
/// </summary>
public static class LegacyRedirects
{
    // Longest prefix first is not needed: matching is on whole segments, so "/gabinet" does not match "/gabinet-programisty".
    static readonly (string Old, string New)[] Map =
    [
        ("/programisci", "/agenci"),
        ("/gabinet", "/agent"),
        ("/rezerwacja", "/dostep"),
        ("/moje/rezerwacje", "/moje/dostepy"),
        ("/gabinet-programisty", "/panel-agenta"),
    ];

    /// <summary>The new path for a legacy path, or null when the path is not a legacy one. The remainder after the prefix is kept as it was written.</summary>
    public static string? Target(PathString path)
    {
        foreach (var (old, @new) in Map)
            if (path.StartsWithSegments(old, StringComparison.OrdinalIgnoreCase, out var rest))
                return @new + rest.Value;
        return null;
    }

    public static IApplicationBuilder UseLegacyRedirects(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) && Target(ctx.Request.Path) is { } target)
        {
            ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            ctx.Response.Headers.Location = ctx.Request.PathBase + target + ctx.Request.QueryString;
            return;
        }
        await next();
    });
}
