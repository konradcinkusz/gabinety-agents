using System.Net.Http.Headers;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Booking.Agents.Fake;
using Gabinety.Booking.Agents.Http;
using Gabinety.Booking.Features.Internal.TestClock;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Agents;

/// <summary>
/// Registers the <see cref="IAgentUpstream"/> named by <c>Agents:Provider</c> (docs/agentic/PLAN.md B-03), the way <c>PaymentsFeature</c> registers the payment provider.
/// <list type="bullet">
/// <item><c>Fake</c>, or no value in the fake profile (<see cref="NonDeployedProfile"/>): <see cref="FakeAgentUpstream"/>. Outside the fake profile no value or <c>Fake</c> is refused
/// when the interface is first resolved (nothing resolves it yet, so a deployed host without the setting still starts).</item>
/// <item><c>Http</c>: <see cref="HttpAgentUpstream"/>; the host stops at startup without <c>Agents:Upstream:BaseUrl</c> (an absolute http or https address) or <c>Agents:Upstream:ApiKey</c>.</item>
/// <item>Any other value: stops the host at startup.</item>
/// </list>
/// <c>Agents:Upstream:TimeoutSeconds</c> (default 600) is the budget of one platform call. The refusal never contains the key.
/// </summary>
public sealed class AgentsFeature : IServiceFeature
{
    public const int DefaultTimeoutSeconds = 600;
    internal const string HttpClientName = "agent-upstream";

    public void Add(IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var provider = config["Agents:Provider"];
        var problem = Problem(provider, NonDeployedProfile.IsActive(config), config);
        var http = problem is null && string.Equals(provider, "Http", StringComparison.OrdinalIgnoreCase);

        if (http)
        {
            builder.Services.AddHttpClient(HttpClientName, c =>
            {
                c.BaseAddress = new Uri(config["Agents:Upstream:BaseUrl"]!.TrimEnd('/') + "/");
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["Agents:Upstream:ApiKey"]!);
                c.Timeout = TimeSpan.FromSeconds(TimeoutSeconds(config));
            });
            builder.Services.AddSingleton<IAgentUpstream>(sp => new HttpAgentUpstream(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        }
        else
        {
            builder.Services.AddSingleton<IAgentUpstream>(sp =>
                problem is not null ? throw new InvalidOperationException(problem) : new FakeAgentUpstream(sp.GetRequiredService<TimeProvider>()));
        }

        // An explicit but wrong setting is refused before the server listens; an absent one in a deployed profile only when the platform is first needed.
        if (problem is not null && !string.IsNullOrWhiteSpace(provider)) builder.Services.AddSingleton<IStartupFilter, ResolveUpstreamAtStartup>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }

    static int TimeoutSeconds(IConfiguration config) =>
        int.TryParse(config["Agents:Upstream:TimeoutSeconds"], out var s) && s > 0 ? s : DefaultTimeoutSeconds;

    /// <summary>The reason the configuration is refused, or null. Names keys, never values.</summary>
    public static string? Problem(string? provider, bool nonDeployed, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase))
            return nonDeployed ? null
                : "Agents:Provider is 'Fake' or not set, and the fake agent platform runs only in the fake profile (GABINETY_FAKE=1 or Environment:Kind=Development). Set Agents:Provider to Http with Agents:Upstream:BaseUrl and Agents:Upstream:ApiKey.";
        if (string.Equals(provider, "Http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(config["Agents:Upstream:BaseUrl"], UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                return "Agents:Provider is 'Http' but Agents:Upstream:BaseUrl is not set or is not an absolute http or https address.";
            if (string.IsNullOrWhiteSpace(config["Agents:Upstream:ApiKey"]))
                return "Agents:Provider is 'Http' but Agents:Upstream:ApiKey (secret Agents__Upstream__ApiKey) is not set.";
            return null;
        }
        return $"Agents:Provider '{provider}' is not known: the providers are Fake (fake profile only) and Http.";
    }

    sealed class ResolveUpstreamAtStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            _ = app.ApplicationServices.GetRequiredService<IAgentUpstream>(); // throws the refusal
            next(app);
        };
    }
}
