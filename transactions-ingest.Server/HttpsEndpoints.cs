using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace transactions_ingest.Server;

/// <summary>
/// Whether the server listens on HTTPS. Read from the addresses the server actually bound, so it covers every way
/// an endpoint can be configured (launch profile URLs, ASPNETCORE_HTTPS_PORTS, Kestrel config) without an
/// environment check.
/// </summary>
public static class HttpsEndpoints
{
    public static bool Any(IServer server) =>
        Any(server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);

    public static bool Any(IEnumerable<string> addresses) =>
        addresses.Any(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
