using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Local.Components;

/// <summary>
/// The local shell part of the connection editor: which shell, what it is started with, where it starts and what it
/// finds in its environment. The shells this computer has are offered by name, beside "what the host's address names".
/// </summary>
public partial class LocalOptionsEditor : ConnectionOptionsEditorBase<LocalConnectionOptions>
{
	/// <summary>The empty choice in the shell list: run whatever the host's address names.</summary>
	private const string AddressChoice = "";

	private readonly FieldDraft<string> _environment = new();
	private IReadOnlyList<LocalShell> _shells = [];
	private IReadOnlyList<string> _choices = [AddressChoice];

	private string ShellValue => Current.Shell?.Trim() ?? AddressChoice;

	private LocalShell? SelectedShell =>
		Current.Shell is { } id && !Current.IsCustom ? LocalShellResolver.Find(_shells, id) : null;

	private bool IsWsl =>
		SelectedShell?.Kind == LocalShellKind.Wsl || LocalShellIds.TryGetDistribution(Current.Shell ?? "", out _);

	private string ShellHelp => Current.IsCustom ? "Runs the program named below."
		: SelectedShell is { } shell ? shell.Program
		: Current.Shell is { Length: > 0 } ? "Not found on this computer. The login keeps it, for a computer that has it."
		: "Empty runs the shell the host's address names, or the default shell for localhost.";

	private string ArgumentsPlaceholder => Current.IsCustom ? "Passed to the program" : "Nothing more";

	private string ArgumentsHelp => Current.IsCustom
		? "Passed to the program exactly as typed."
		: "Added after the ones the shell starts with, exactly as typed.";

	private string FolderPlaceholder => IsWsl ? "~, your Linux home" : "Your profile folder";

	private string FolderHelp => IsWsl
		? "A Linux path, ~, or a Windows folder. Empty takes the one in Settings."
		: "A full path, or ~ for your profile folder. Empty takes the one in Settings.";

	private string EnvironmentHelp => IsWsl
		? "Added to the shell's environment and passed into Linux through WSLENV. NAME= with nothing after it removes one."
		: "Added to the shell's environment. NAME= with nothing after it removes one.";

	protected override LocalConnectionOptions From(ProtocolOptions options) => LocalConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(LocalConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>The list always shows what this login has, even a shell this computer does not.</summary>
	protected override void OnCurrentChanged() => SyncChoices();

	protected override void OnInitialized() => RefreshShells();

	private void RefreshShells()
	{
		_shells = LocalShellDetector.Detect(LocalMachine.Current());
		SyncChoices();
	}

	private void SyncChoices()
	{
		List<string> choices = [AddressChoice, .. _shells.Select(shell => shell.Id), LocalShellIds.Custom];

		// A shell installed on another computer, such as a WSL distribution this one lacks, stays picked as it is.
		if (ShellValue.Length > 0 && !choices.Contains(ShellValue, StringComparer.OrdinalIgnoreCase))
		{
			choices.Add(ShellValue);
		}

		_choices = choices;
	}

	private string ShellLabel(string id)
	{
		if (id.Length == 0)
		{
			return "What the host's address names";
		}

		if (id.Equals(LocalShellIds.Custom, StringComparison.OrdinalIgnoreCase))
		{
			return "Another program";
		}

		return _shells.FirstOrDefault(shell => shell.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DisplayName
			?? $"{id} (not on this computer)";
	}

	private Task OnShellChangedAsync(string? value) =>
		ChangeAsync(Current with { Shell = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });

	private Task OnProgramChangedAsync(string? value) => ChangeAsync(Current with { Program = value });

	private Task OnArgumentsChangedAsync(string? value) => ChangeAsync(Current with { Arguments = value });

	private Task OnFolderChangedAsync(string? value) => ChangeAsync(Current with { StartingFolder = value });

	private Task OnEnvironmentChangedAsync(string? value)
	{
		if (!LocalEnvironmentVariables.TryParse(value, out _, out string? error))
		{
			_environment.Reject(value ?? "", error);
			return Task.CompletedTask;
		}

		_environment.Clear();
		return ChangeAsync(Current with { EnvironmentVariables = value });
	}
}
