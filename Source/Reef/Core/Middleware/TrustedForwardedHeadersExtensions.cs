using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Reef.Core.Models;
using Reef.Core.Security;
using Reef.Helpers;
using Serilog;

namespace Reef.Core.Middleware;

/// <summary>
/// Honours X-Forwarded headers only from the proxies in KNOWN_PROXIES and KNOWN_NETWORKS
/// </summary>
public static class TrustedForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxySettings>()
            .Configure<IConfiguration>((settings, config) =>
            {
                settings.KnownProxies = config.GetList("Reef:KnownProxies");
                settings.KnownNetworks = config.GetList("Reef:KnownNetworks");
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrustedProxySettings>, TrustedProxySettingsValidator>();
        return services;
    }

    public static WebApplication UseTrustedForwardedHeaders(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<TrustedProxySettings>>().Value;
        if (settings.KnownProxies.Length == 0 && settings.KnownNetworks.Length == 0)
        {
            Log.Warning("No trusted proxies configured, X-Forwarded headers are ignored. Set KNOWN_PROXIES or KNOWN_NETWORKS behind a reverse proxy.");
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            RequireHeaderSymmetry = false,
            ForwardLimit = null,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in settings.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in settings.KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

        Log.Information("Trusting X-Forwarded headers from {ProxyCount} proxies and {NetworkCount} networks", settings.KnownProxies.Length, settings.KnownNetworks.Length);
        app.UseForwardedHeaders(options);
        return app;
    }
}
