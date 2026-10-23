namespace Reef.Core.Models;

public sealed class TrustedProxySettings
{
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
}
