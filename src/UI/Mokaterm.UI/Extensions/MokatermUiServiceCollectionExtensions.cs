using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Theming;
using Moka.Red.Extensions;
using Moka.Red.Navigation.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Connections;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.Fleet;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.QuickConnect;
using Mokaterm.UI.Settings.Extensions;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Terminal.Extensions;
using Mokaterm.UI.Tools;
using Mokaterm.UI.Vault;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Extensions;

public static class MokatermUiServiceCollectionExtensions
{
	/// <summary>
	/// The scoped services of the shell that belong to one page rather than to the window's state. A second page that
	/// borrows a window's services, such as a tool's own window on the desktop, gets its own of these and the window's
	/// of everything else. The prompt queue is one: a question a tool asks shows in the window the tool is in.
	/// Services that call into the page, Blazor's and Moka.Red's are recognized without being listed here.
	/// </summary>
	public static IReadOnlyList<Type> PageServices { get; } = [typeof(IUserInteraction), typeof(UserInteractionService)];

	/// <summary>
	/// Registers the shell and the UI libraries it hosts: Moka.Red, tabs, terminal, file browser and the built-in
	/// settings pages. Hosts call it once, next to the core and module registrations, and render <see cref="MokatermApp"/>.
	/// The shell's <see cref="IUserInteraction"/> replaces any earlier registration.
	/// </summary>
	public static IServiceCollection AddMokatermUI(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokaRed(options => options.Theme = MokaTheme.Dark);
		services.AddMokaTabs<Guid>();
		services.AddMokatermUiCommon();
		services.AddMokatermTerminal();
		services.AddMokatermFileBrowser();
		services.AddMokatermSettingsPages();

		services.AddScoped<UserInteractionService>();
		services.AddScoped<IUserInteraction>(provider => provider.GetRequiredService<UserInteractionService>());
		services.AddScoped<UiStateStore>();
		services.AddScoped<ShellState>();
		services.AddScoped<ISettingsNavigator, ShellSettingsNavigator>();
		services.AddScoped<ShellInterop>();
		services.AddScoped<ShellInputBridge>();
		services.AddScoped<ShellCommandRegistry>();
		services.AddScoped<SessionWorkspace>();
		services.AddScoped<SessionRestore>();
		services.AddScoped<InputBroadcast>();
		services.AddScoped<SessionActions>();
		services.AddScoped<ISessionTabs, SessionTabs>();

		// Tools in dialogs and in windows of their own. All three are the window's state, which a tool window borrows.
		services.AddScoped<ShellToolWindows>();
		services.AddScoped<SessionToolActions>();
		services.AddScoped<AppToolActions>();
		services.AddScoped<ToolWindowRestore>();

		services.AddScoped<SessionViewRequests>();
		services.AddScoped<CatalogState>();
		services.AddScoped<ConnectionActions>();
		services.AddScoped<QuickConnectHistory>();
		services.AddScoped<CommandSnippetActions>();
		services.AddScoped<FleetOverview>();
		services.AddScoped<VaultScreenState>();
		services.AddScoped<DamagedDocumentNotices>();
		services.AddScoped<PluginNotices>();

		// The shell's own settings page: it lists the shell's commands, which only this assembly knows.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "keyboard",
			Title = "Keyboard",
			Description = "The key combination for each command. Ctrl and a letter stays with the terminal.",
			Icon = MokatermIcons.Keyboard,
			Component = typeof(KeyboardSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 15,
			Keywords = "shortcut shortcuts keys keyboard binding gesture hotkey",
		});

		return services;
	}
}
