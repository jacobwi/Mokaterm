using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Mokaterm.Web.Services;

/// <summary>
/// Takes the client address and scheme from <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c>, but only from a proxy
/// on this machine (ASP.NET Core's default) or one listed under <c>Mokaterm:ReverseProxy</c>. Behind a proxy that ends
/// TLS every request would otherwise look like plain HTTP, so HSTS would never be sent.
/// </summary>
internal static class ReverseProxy
{
	public const string SectionName = "Mokaterm:ReverseProxy";

	/// <exception cref="InvalidOperationException">An entry is not an address or a network; the host should not start.</exception>
	public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
	{
		options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
		IConfigurationSection section = configuration.GetSection(SectionName);
		foreach (string proxy in Values(section, "KnownProxies"))
		{
			options.KnownProxies.Add(IPAddress.TryParse(proxy, out IPAddress? address)
				? address
				: throw new InvalidOperationException($"{SectionName}:KnownProxies holds '{proxy}', which is not an IP address."));
		}

		foreach (string network in Values(section, "KnownNetworks"))
		{
			if (!System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork parsed))
			{
				throw new InvalidOperationException($"{SectionName}:KnownNetworks holds '{network}', which is not a network such as 10.0.0.0/8.");
			}

			options.KnownIPNetworks.Add(parsed);
		}
	}

	private static IEnumerable<string> Values(IConfigurationSection section, string key) =>
		section.GetSection(key).GetChildren()
			.Select(child => child.Value?.Trim())
			.OfType<string>()
			.Where(value => value.Length > 0);
}
