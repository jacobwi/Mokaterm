using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Settings.Pages.About;
using Mokaterm.UI.Settings.Pages.Appearance;
using Mokaterm.UI.Settings.Pages.Commands;
using Mokaterm.UI.Settings.Pages.Files;
using Mokaterm.UI.Settings.Pages.General;
using Mokaterm.UI.Settings.Pages.Keychain;
using Mokaterm.UI.Settings.Pages.KnownHosts;
using Mokaterm.UI.Settings.Pages.Security;
using Mokaterm.UI.Settings.Pages.SessionLogs;
using Mokaterm.UI.Settings.Pages.Terminal;

namespace Mokaterm.UI.Settings.Extensions;

public static class SettingsPagesServiceCollectionExtensions
{
	/// <summary>Registers the built-in settings pages as descriptors. Called by the shell's <c>AddMokatermUI()</c>.</summary>
	public static IServiceCollection AddMokatermSettingsPages(this IServiceCollection services)
	{
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "general",
			Title = "General",
			Description = "Closing sessions, reconnecting and the recent connections list.",
			Icon = MokatermIcons.Sliders,
			Component = typeof(GeneralSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 0,
			Keywords = "confirm close sessions tabs reconnect automatic retry delay recent connections history",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "appearance",
			Title = "Appearance",
			Description = "Theme, accent color, density and text size for the whole app. Terminal colors are on the Terminal page.",
			Icon = MokatermIcons.Palette,
			Component = typeof(AppearanceSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 10,
			Keywords = "theme dark light mode accent color colour density compact cozy comfortable font scale text size zoom status bar",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "terminal",
			Title = "Terminal",
			Description = "Colors, font, cursor and input handling for every terminal session. A saved connection can override the theme and font.",
			Icon = MokatermIcons.Terminal,
			Component = typeof(TerminalSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 20,
			Keywords = "terminal theme colors custom theme font family size line height letter spacing cursor blink scrollback "
				+ "copy on select right click paste bell multi-line webgl renderer term type replay buffer",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "commands",
			Title = "Saved commands",
			Description = "Commands you kept from a terminal, with the machines and logins they belong to. They are encrypted in the vault, so a command with a token in it stays a secret.",
			Icon = MokatermIcons.Command,
			Component = typeof(CommandsPage),
			Group = SettingsPageGroup.Application,
			Order = 25,
			Keywords = "saved commands snippets notes history run type scope global machine host login tags favourites",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "files",
			Title = "Files & transfers",
			Description = "The remote file browser, confirmations before risky actions, and how uploads and downloads run.",
			Icon = MokatermIcons.Transfers,
			Component = typeof(FileTransferSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 30,
			Keywords = "file browser sftp ftp hidden files dotfiles folders first drag drop upload download confirm delete "
				+ "overwrite timestamps parallel concurrent transfers download folder",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "session-logs",
			Title = "Session logs",
			Description = "Recording what a session's terminal shows into a file. Off by default, because the file holds everything the server printed.",
			Icon = MokatermIcons.Record,
			Component = typeof(SessionLogSettingsPage),
			Group = SettingsPageGroup.Application,
			Order = 35,
			Keywords = "session log logging record transcript capture output file folder size limit plain text raw escape sequences save download",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "security",
			Title = "Security",
			Description = "When the vault locks, how long copied passwords stay on the clipboard, and which servers to trust.",
			Icon = MokatermIcons.Shield,
			Component = typeof(SecuritySettingsPage),
			Group = SettingsPageGroup.Security,
			Order = 0,
			Keywords = "vault master password change lock auto-lock idle hidden minimized clipboard clear host key policy "
				+ "strict accept new sudo device unlock reset",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "keychain",
			Title = "Keychain",
			Description = "Passwords and private keys that any number of logins can share. A credential that belongs to a single login is edited with that login.",
			Icon = MokatermIcons.Key,
			Component = typeof(KeychainPage),
			Group = SettingsPageGroup.Security,
			Order = 10,
			Keywords = "credentials passwords private keys ssh keys passphrase identities fingerprint copy password",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "known-hosts",
			Title = "Known hosts",
			Description = "SSH host keys and TLS certificates you chose to trust. Mokaterm warns when a server presents something different.",
			Icon = MokatermIcons.ShieldCheck,
			Component = typeof(KnownHostsPage),
			Group = SettingsPageGroup.Security,
			Order = 20,
			Keywords = "known hosts host keys fingerprints certificates tls trusted servers remove",
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "about",
			Title = "About",
			Description = "Version, runtime, loaded modules and protocols, and the open source projects Mokaterm is built on.",
			Icon = MokatermIcons.Modules,
			Component = typeof(AboutPage),
			Group = SettingsPageGroup.About,
			Order = 0,
			Keywords = "about version platform runtime dotnet modules protocols credits licenses data folder",
		});

		return services;
	}
}
