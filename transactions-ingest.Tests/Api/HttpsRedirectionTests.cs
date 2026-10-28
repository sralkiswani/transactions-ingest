using System.Net;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using transactions_ingest.Server;

namespace transactions_ingest.Tests.Api;

/// <summary>HTTPS redirection follows the addresses the server actually listens on.</summary>
public class HttpsRedirectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task Http_only_server_serves_requests_without_redirecting_or_warning()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new ApiFactory
        {
            EnvironmentName = "Production",
            ConfigureServices = s => s.AddSingleton<ILoggerProvider>(logs),
        };
        var client = factory.CreateClient(NoRedirects);

        using var response = await client.GetAsync("/api/transactions", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("https port", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Server_with_an_https_address_redirects_http_requests_to_it()
    {
        using var factory = new ApiFactory { EnvironmentName = "Production" };
        // Start the server, then give it the addresses Kestrel would report for the https profile.
        var features = factory.Server.Features;
        var addresses = features.Get<IServerAddressesFeature>() ?? new ServerAddressesFeature();
        addresses.Addresses.Add("https://localhost:7103");
        addresses.Addresses.Add("http://localhost:5291");
        features.Set(addresses);
        var client = factory.CreateClient(NoRedirects);

        using var response = await client.GetAsync("/api/transactions", Ct);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost:7103/api/transactions", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData(new[] { "https://localhost:7103", "http://localhost:5291" }, true)]
    [InlineData(new[] { "HTTPS://[::]:8081" }, true)]
    [InlineData(new[] { "http://[::]:8080" }, false)]
    [InlineData(new string[0], false)]
    public void Https_endpoint_detection(string[] addresses, bool expected)
    {
        Assert.Equal(expected, HttpsEndpoints.Any(addresses));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (provider._messages)
                {
                    provider._messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
