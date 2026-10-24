using System.Net;
using Microsoft.Extensions.Options;
using Reef.Core.Models;

namespace Reef.Core.Security;

public sealed class TrustedProxySettingsValidator : IValidateOptions<TrustedProxySettings>
{
    public ValidateOptionsResult Validate(string? name, TrustedProxySettings options)
    {
        var failures = options.KnownProxies.Where(p => !IPAddress.TryParse(p, out _))
            .Select(p => $"KNOWN_PROXIES entry '{p}' is not an IP address")
            .Concat(options.KnownNetworks.Where(n => !IPNetwork.TryParse(n, out _))
                .Select(n => $"KNOWN_NETWORKS entry '{n}' is not a network in CIDR notation, such as 172.16.0.0/12"))
            .ToList();
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
