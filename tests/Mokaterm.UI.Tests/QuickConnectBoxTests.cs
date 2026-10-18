using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>The quick connect box starts on the protocol picked last, while this host still has it.</summary>
public sealed class QuickConnectBoxTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task StartsOnTheProtocolPickedLast()
	{
		string html = await RenderAsync(new UiStateDocument { QuickConnectProtocolId = "vnc" });

		// The field's example follows the picker, so it says which protocol the box starts on.
		Assert.Contains("placeholder=\"host:5900\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AProtocolThisHostNoLongerHas_StartsOnSsh()
	{
		string html = await RenderAsync(new UiStateDocument { QuickConnectProtocolId = "telnet" });

		Assert.Contains($"placeholder=\"{QuickConnectInput.DefaultExample}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NothingPickedYet_StartsOnSsh()
	{
		string html = await RenderAsync(null);

		Assert.Contains($"placeholder=\"{QuickConnectInput.DefaultExample}\"", html, StringComparison.Ordinal);
	}

	private static async Task<string> RenderAsync(UiStateDocument? saved)
	{
		MemoryAppDataStore store = new();
		if (saved is not null)
		{
			store.Set("ui-state", saved);
		}

		await using UiStateStore uiState = new(store, TimeProvider.System, NullLogger<UiStateStore>.Instance);
		await uiState.LoadAsync(Ct).WaitAsync(Patience, Ct);

		return await StaticRender.RenderAsync<QuickConnectBox>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IVaultDataStore, MemoryVaultDataStore>();
				services.AddSingleton<IConnectionRepository>(new EditableCatalog(ConnectionCatalog.Empty));
				services.AddSingleton<IProtocolRegistry>(new QuickConnectProtocols());
				services.AddSingleton<ISettingsService>(new FixedSettings());
				services.AddSingleton<IAppDataStore>(store);
				services.AddSingleton(uiState);
				services.AddScoped<ISessionManager, ManualSessionManager>();
				services.AddScoped<SessionWorkspace>();
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<SessionRestore>();
				services.AddScoped<ShellState>();
				services.AddScoped<CatalogState>();
				services.AddScoped<SessionActions>();
				services.AddScoped<QuickConnectHistory>();
				services.AddScoped<FormInterop>();
				services.AddScoped<ShellInterop>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}
}
