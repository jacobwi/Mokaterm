using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

public static class PluginGalleryServiceCollectionExtensions
{
	/// <summary>
	/// Adds the plugin gallery the Plugins page browses: one HTTP client for the app, and the list kept per window or
	/// circuit. Only a host where the user installs plugins calls it; without it the page has no Browse tab.
	/// </summary>
	/// <param name="services">The host's services.</param>
	/// <param name="options">Where the list is and which keys it must be signed with.</param>
	/// <param name="handler">
	/// Makes the handler every request goes through, or null for the network. The DevHost serves its demo gallery through
	/// one; nothing that ships passes it.
	/// </param>
	/// <exception cref="ArgumentException">The list's address is not https, or a key is not an Ed25519 public key.</exception>
	public static IServiceCollection AddPluginGallery(this IServiceCollection services, PluginGalleryOptions options, Func<HttpMessageHandler>? handler = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);

		// Checked here so a host with a wrong address or key fails at start, not when someone opens the Browse tab.
		PluginGalleryClient.CheckIndex(options.Index);
		PluginIndexSignature.ReadKeys(options.TrustedKeys);

		services.TryAddSingleton(_ => new PluginGalleryClient(options, handler?.Invoke()));
		services.TryAddScoped<IPluginGallery, PluginGallery>();
		return services;
	}
}
