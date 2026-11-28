namespace Gabinety.DemoSeeder;

/// <summary>
/// The API key of <c>demo-client</c> (D-01, PLAN B-01): one key named <see cref="KeyName"/>, created through <c>POST /api/v1/me/api-keys</c> as the client would. The service shows the key once, in the answer;
/// the seeder reads only its 8-character prefix and logs that (rule 7: the key is never stored or logged). Idempotent: a key with that name that is still active is found again by the list, which never has the key,
/// so a second run creates nothing and prints the same prefix. Reset needs nothing of its own: Booking's demo reset removes the keys of <c>demo-</c> users together with their bookings.
/// </summary>
public sealed class ApiKeySeedStep : ISeedStep
{
    public const string KeyName = "demo-seed-key", ClientName = "demo-client";

    public string Name => "apikey";
    public int Order => 250; // after Booking (a key is of no use before the client has a package) and before Reputation

    public Task<int> ResetAsync(SeedContext context, CancellationToken ct) => Task.FromResult(context.Options.AccessUrl.Length == 0 ? -1 : 0);

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.AccessUrl.Length == 0) { context.Log.Info("apikey: skipped (Backend:Booking is not configured)"); return; }
        var api = new AccessApi(context.Http.For(SeedService.Access), context.Options, context.Time);
        var client = context.Clients.TryGetValue(ClientName, out var c) ? c : throw new SeedException($"apikey: {ClientName} is unknown (run the directory step first)");
        DemoNames.Require(KeyName);

        if ((await api.ApiKeysAsync(client, ct)).FirstOrDefault(k => k.Name == KeyName) is { } found)
        {
            context.Log.Info($"apikey: {ClientName} key {found.Name} found, prefix {found.Prefix}");
            return;
        }
        var created = await api.CreateApiKeyAsync(client, KeyName, ct);
        context.Log.Info($"apikey: {ClientName} key {created.Name} created, prefix {created.Prefix} (the key is shown once and not kept)");
    }
}
