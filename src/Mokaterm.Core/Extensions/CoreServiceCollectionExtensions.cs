using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Commands;
using Mokaterm.Core.Connections;
using Mokaterm.Core.Credentials;
using Mokaterm.Core.Import;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Security;
using Mokaterm.Core.Settings;
using Mokaterm.Core.Storage;
using Mokaterm.Core.Terminal;

namespace Mokaterm.Core.Extensions;

/// <summary>Registers Mokaterm's data and security services. Hosts call <c>AddMokaterm</c> once at startup.</summary>
public static class CoreServiceCollectionExtensions
{
	/// <summary>
	/// Registers the vault, stores, repositories, settings and theme catalog. The host must register
	/// <see cref="Abstractions.Platform.IAppEnvironment"/> and may register <see cref="IDeviceKeyProtector"/>; the shell
	/// registers <see cref="Abstractions.Interaction.IUserInteraction"/>. Safe to call twice.
	/// </summary>
	public static IServiceCollection AddMokatermCore(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton(new VaultOptions());
		services.TryAddSingleton<ISshKeyGenerator, SshKeyGenerator>();

		// Import sources only read; the importer they feed is scoped because it writes through the repository.
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionImportSource, OpenSshConfigImportSource>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionImportSource, PuttyImportSource>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionImportSource, WinScpImportSource>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IConnectionImportSource, ConnectionBundleImportSource>());

		// Shared by every UI scope.
		services.TryAddSingleton<IAppDataStore, FileAppDataStore>();
		services.TryAddSingleton<ISettingsService, SettingsService>();
		services.TryAddSingleton<ITerminalThemeCatalog, TerminalThemeCatalog>();
		services.TryAddSingleton<ITerminalThemeConverter, TerminalThemeConverter>();
		services.TryAddSingleton<VaultHeaderStore>();
		services.TryAddSingleton<UnlockThrottle>();
		services.TryAddSingleton<VaultDocumentFiles>();
		services.TryAddSingleton<VaultDocumentChangeNotifier>();
		services.TryAddSingleton<VaultDocumentUpdateLocks>();

		// One per UI scope: a MAUI window or a Blazor Server circuit.
		services.TryAddScoped<Vault>();
		services.TryAddScoped<IVault>(static provider => provider.GetRequiredService<Vault>());
		services.TryAddScoped<IVaultCipher>(static provider => provider.GetRequiredService<Vault>());
		services.TryAddScoped<VaultDataStore>();
		services.TryAddScoped<IVaultDataStore>(static provider => provider.GetRequiredService<VaultDataStore>());
		services.TryAddScoped<IKnownHostsStore, KnownHostsStore>();
		services.TryAddScoped<IHostIdentityVerifier, HostIdentityVerifier>();
		services.TryAddScoped<ICredentialStore, CredentialStore>();
		services.TryAddScoped<IConnectionRepository, ConnectionRepository>();
		services.TryAddScoped<IConnectionImporter, ConnectionImporter>();
		services.TryAddScoped<IConnectionExporter, ConnectionExporter>();

		// The repository takes the store itself: deleting a login or a machine cleans up the commands saved under it,
		// which is maintenance the UI never asks for.
		services.TryAddScoped<CommandSnippetStore>();
		services.TryAddScoped<ICommandSnippetStore>(static provider => provider.GetRequiredService<CommandSnippetStore>());

		return services;
	}

	/// <summary>Registers Core, then lets <paramref name="configure"/> add modules through <see cref="IMokatermBuilder"/>.</summary>
	public static IServiceCollection AddMokaterm(this IServiceCollection services, Action<IMokatermBuilder>? configure = null)
	{
		services.AddMokatermCore();
		configure?.Invoke(new MokatermBuilder(services));
		return services;
	}
}
