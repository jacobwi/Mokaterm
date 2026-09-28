using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Changing the global shortcuts. A row records the next key combination while it has focus, which is also why the
/// shell's own listener is off during that time.
/// </summary>
public sealed partial class KeyboardSettingsPage : SettingsSectionBase<ShortcutSettings>, IAsyncDisposable
{
	private string? _recording;
	private string? _message;

	/// <summary>Leaving the page while a row is recording must not leave the shortcuts switched off.</summary>
	public async ValueTask DisposeAsync()
	{
		if (_recording is not null)
		{
			_recording = null;
			await Bridge.SetShortcutsEnabledAsync(true);
		}
	}

	[Inject]
	private ShellCommandRegistry Registry { get; set; } = default!;

	[Inject]
	private ShellInputBridge Bridge { get; set; } = default!;

	private IReadOnlyList<ShellCommandInfo> Commands => Registry.Commands;

	private bool IsChanged(string commandId) => Settings.Bindings.ContainsKey(commandId);

	private string ButtonClass(string commandId) =>
		_recording == commandId ? "mt-shortcut mt-shortcut--recording" : "mt-shortcut";

	private string ButtonText(string commandId) =>
		_recording == commandId ? "Press a key" : ShellShortcuts.LabelFor(commandId) ?? "None";

	private async Task StartRecordingAsync(string commandId)
	{
		_recording = commandId;
		_message = null;
		await Bridge.SetShortcutsEnabledAsync(false);
	}

	private async Task StopRecordingAsync()
	{
		_recording = null;
		await Bridge.SetShortcutsEnabledAsync(true);
	}

	private async Task OnKeyDownAsync(string commandId, KeyboardEventArgs args)
	{
		if (_recording != commandId || IsModifier(args.Code))
		{
			return;
		}

		if (args.Code == "Escape")
		{
			_message = null;
			return;
		}

		if (args.Code is "Backspace" or "Delete")
		{
			await SaveAsync(commandId, ShortcutGesture.None);
			_message = null;
			return;
		}

		if (!args.CtrlKey && !args.MetaKey)
		{
			_message = "Hold Ctrl as well, or Cmd on macOS.";
			return;
		}

		if (args.AltKey)
		{
			// Ctrl+Alt is AltGr on Windows keyboards, and those keystrokes type characters.
			_message = "Alt cannot be part of a shortcut.";
			return;
		}

		ShortcutGesture gesture = new(args.Code, args.ShiftKey);
		if (!ShellShortcuts.IsAllowed(gesture))
		{
			_message = "A letter needs Shift too, because Ctrl and a letter is what the terminal gets.";
			return;
		}

		string? previous = ShellShortcuts.CommandFor(gesture);
		await SaveAsync(commandId, gesture);

		// Recording stops on the key that was accepted, so the row shows what it now carries instead of "Press a key".
		await StopRecordingAsync();
		_message = previous is null || previous == commandId
			? null
			: string.Create(
				CultureInfo.CurrentCulture,
				$"{ShellShortcuts.Describe(gesture)} was {TitleOf(previous)}, which has no shortcut now.");
	}

	private async Task SaveAsync(string commandId, ShortcutGesture gesture)
	{
		Dictionary<string, ShortcutGesture> bindings = new(Settings.Bindings, StringComparer.Ordinal)
		{
			[commandId] = gesture,
		};

		await ApplyAsync(bindings);
	}

	private async Task ResetOneAsync(string commandId)
	{
		Dictionary<string, ShortcutGesture> bindings = new(Settings.Bindings, StringComparer.Ordinal);
		if (!bindings.Remove(commandId))
		{
			return;
		}

		_message = null;
		await ApplyAsync(bindings);
	}

	private async Task ResetAllAsync()
	{
		_message = null;
		await ResetAsync();
		ShellShortcuts.Apply(Settings);
	}

	private async Task ApplyAsync(Dictionary<string, ShortcutGesture> bindings)
	{
		await UpdateAsync(settings => settings with { Bindings = bindings });

		// The bridge reattaches the listeners on its own; this is what makes the labels on this page and in menus follow
		// straight away.
		ShellShortcuts.Apply(Settings);
	}

	private string TitleOf(string commandId)
	{
		foreach (ShellCommandInfo command in Commands)
		{
			if (command.Id == commandId)
			{
				return command.Title;
			}
		}

		return commandId;
	}

	private static bool IsModifier(string code) =>
		code.StartsWith("Control", StringComparison.Ordinal)
		|| code.StartsWith("Shift", StringComparison.Ordinal)
		|| code.StartsWith("Alt", StringComparison.Ordinal)
		|| code.StartsWith("Meta", StringComparison.Ordinal)
		|| code.StartsWith("OS", StringComparison.Ordinal);
}
