using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Feedback.CommandPalette;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The palette reaches every session tool and every toolbar button, so a button hidden from the bar is never lost, and
/// none of them takes a default key the rules do not allow.
/// </summary>
public sealed class ToolCommandsTests
{
	[Fact]
	public void EveryTool_GetsOneOpenCommand_EvenWhenSeveralProtocolsShareIt()
	{
		IReadOnlyList<ShellCommandInfo> commands = Registry(
			Tool("ssh", "docker", "Docker"),
			Tool("demo", "docker", "Docker"),
			Tool("ssh", "system", "System")).Commands;

		ShellCommandInfo docker = Assert.Single(commands, command => command.Id == "mokaterm.tool.docker");
		Assert.Equal("Open Docker", docker.Title);
		Assert.Equal("Tools", docker.Group);
		Assert.Single(commands, command => command.Id == "mokaterm.tool.system");
	}

	[Fact]
	public void AToolWithoutAnId_IsNamedByItsTitle()
	{
		IReadOnlyList<ShellCommandInfo> commands = Registry(Tool("ssh", null, "Port Forwarding")).Commands;

		Assert.Contains(commands, command => command.Id == "mokaterm.tool.port-forwarding");
	}

	[Theory]
	[InlineData(ShellCommandIds.ToggleFileBrowser)]
	[InlineData(ShellCommandIds.FindInTerminal)]
	[InlineData(ShellCommandIds.TerminalTheme)]
	[InlineData(ShellCommandIds.ClearTerminal)]
	[InlineData(ShellCommandIds.RecordSession)]
	[InlineData(ShellCommandIds.SavedCommands)]
	[InlineData(ShellCommandIds.BroadcastInput)]
	[InlineData(ShellCommandIds.Duplicate)]
	[InlineData(ShellCommandIds.Disconnect)]
	[InlineData(ShellCommandIds.CloseTab)]
	public void EveryToolbarButton_HasAPaletteCommand(string id) =>
		Assert.Contains(Registry().Commands, command => command.Id == id);

	// Tools come from modules and change with them; a default gesture would be taken from whatever held it.
	[Fact]
	public void NoToolCommand_HasADefaultKey() =>
		Assert.DoesNotContain(ShellShortcuts.DefaultBindings, binding => binding.Id.StartsWith(ShellCommandIds.ToolPrefix, StringComparison.Ordinal));

	private static SessionToolDescriptor Tool(string protocol, string? id, string title) => new()
	{
		ProtocolId = protocol,
		Id = id,
		Title = title,
		Icon = MokatermIcons.Modules,
		Component = typeof(NoView),
	};

	private static ShellCommandRegistry Registry(params SessionToolDescriptor[] tools)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		ManualSessionManager sessions = new();
		SessionWorkspace workspace = new(sessions);
		UserInteractionService interaction = new();
		SessionActions actions = new(sessions, workspace, null!, null!, interaction, null!, shell, NullLogger<SessionActions>.Instance);
		return new ShellCommandRegistry(
			new MokaCommandPaletteService(),
			shell,
			workspace,
			actions,
			null!,
			null!,
			null!,
			null!,
			null!,
			new UiContributions([], [], tools),
			interaction,
			null!,
			null!,
			new SessionViewRequests());
	}

	/// <summary>A tool's component; these tests never render it.</summary>
	private sealed class NoView : SessionViewBase;
}
