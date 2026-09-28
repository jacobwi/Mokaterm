using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The global shortcuts: the defaults below with the user's own bindings on top. Every gesture is the primary modifier
/// (Ctrl, Cmd on macOS) plus an optional Shift, because a plain Ctrl+letter is terminal input and the shell running in a
/// focused terminal must keep it. Settings are one file per installation, so the gestures in force are process wide.
/// </summary>
internal static class ShellShortcuts
{
	private const int MaxCodeLength = 24;

	public static IReadOnlyList<ShellShortcutBinding> DefaultBindings { get; } =
	[
		new(ShellCommandIds.CommandPalette, "KeyP", Shift: true),
		new(ShellCommandIds.NewLogin, "KeyN", Shift: true),
		new(ShellCommandIds.NewHost, "KeyH", Shift: true),
		new(ShellCommandIds.QuickConnect, "KeyO", Shift: true),
		new(ShellCommandIds.LockVault, "KeyL", Shift: true),
		new(ShellCommandIds.Settings, "Comma", Shift: false),
		new(ShellCommandIds.ToggleConnections, "KeyE", Shift: true),
		new(ShellCommandIds.ToggleTransfers, "KeyX", Shift: true),
		new(ShellCommandIds.NextTab, "Tab", Shift: false),
		new(ShellCommandIds.PreviousTab, "Tab", Shift: true),
		new(ShellCommandIds.CloseTab, "KeyW", Shift: true),
		new(ShellCommandIds.Reconnect, "KeyR", Shift: true),
		new(ShellCommandIds.Duplicate, "KeyD", Shift: true),
		new(ShellCommandIds.SavedCommands, "KeyS", Shift: true),
		new(ShellCommandIds.BroadcastInput, "KeyB", Shift: true),
		new(ShellCommandIds.MoveToOtherPane, "Backslash", Shift: true),
		new(ShellCommandIds.Shortcuts, "KeyK", Shift: true),
	];

	// Declared after the defaults: a static field is initialized in declaration order, so the other way round it
	// would start out null.
	private static IReadOnlyList<ShellShortcutBinding> _bindings = DefaultBindings;

	/// <summary>The gestures in force.</summary>
	public static IReadOnlyList<ShellShortcutBinding> Bindings => Volatile.Read(ref _bindings);

	/// <summary>Puts the saved bindings in force for every window and circuit.</summary>
	public static void Apply(ShortcutSettings settings) => Volatile.Write(ref _bindings, Merge(settings));

	/// <summary>
	/// The defaults with <paramref name="settings"/> on top: an entry with an empty code turns that command's shortcut
	/// off, and a gesture that another command already had moves to the command that names it.
	/// </summary>
	public static IReadOnlyList<ShellShortcutBinding> Merge(ShortcutSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		List<ShellShortcutBinding> merged = [.. DefaultBindings];
		foreach ((string commandId, ShortcutGesture gesture) in settings.Bindings)
		{
			if (string.IsNullOrWhiteSpace(commandId))
			{
				continue;
			}

			merged.RemoveAll(binding => string.Equals(binding.Id, commandId, StringComparison.Ordinal));
			if (gesture is null || !IsAllowed(gesture))
			{
				// An empty or refused gesture leaves the command without one, which is what "off" means here.
				continue;
			}

			merged.RemoveAll(binding => binding.Code == gesture.Code && binding.Shift == gesture.Shift);
			merged.Add(new ShellShortcutBinding(commandId, gesture.Code, gesture.Shift));
		}

		return merged;
	}

	/// <summary>
	/// Whether a gesture can be bound at all: a letter needs Shift, because Ctrl and a letter is what a program running
	/// in the terminal expects to receive.
	/// </summary>
	public static bool IsAllowed(ShortcutGesture gesture)
	{
		if (gesture is not { IsSet: true } || gesture.Code.Length > MaxCodeLength)
		{
			return false;
		}

		foreach (char character in gesture.Code)
		{
			if (!char.IsAsciiLetterOrDigit(character))
			{
				return false;
			}
		}

		return gesture.Shift || !IsLetter(gesture.Code);
	}

	/// <summary>The gesture in force for a command, or <see cref="ShortcutGesture.None"/>.</summary>
	public static ShortcutGesture GestureFor(string commandId)
	{
		foreach (ShellShortcutBinding binding in Bindings)
		{
			if (string.Equals(binding.Id, commandId, StringComparison.Ordinal))
			{
				return new ShortcutGesture(binding.Code, binding.Shift);
			}
		}

		return ShortcutGesture.None;
	}

	/// <summary>The command a gesture already belongs to, so the settings page can say what it would take over.</summary>
	public static string? CommandFor(ShortcutGesture gesture)
	{
		ArgumentNullException.ThrowIfNull(gesture);
		foreach (ShellShortcutBinding binding in Bindings)
		{
			if (binding.Code == gesture.Code && binding.Shift == gesture.Shift)
			{
				return binding.Id;
			}
		}

		return null;
	}

	/// <summary>The keys for <paramref name="commandId"/>, such as <c>["Ctrl", "Shift", "N"]</c>, or empty.</summary>
	public static IReadOnlyList<string> KeysFor(string commandId)
	{
		ShortcutGesture gesture = GestureFor(commandId);
		return gesture.IsSet ? Keys(gesture) : [];
	}

	/// <summary>Display text such as <c>Ctrl+Shift+N</c>, or null when the command has no shortcut.</summary>
	public static string? LabelFor(string commandId)
	{
		IReadOnlyList<string> keys = KeysFor(commandId);
		return keys.Count == 0 ? null : string.Join('+', keys);
	}

	/// <summary>Display text for a gesture that is not bound yet, such as the one being recorded.</summary>
	public static string Describe(ShortcutGesture gesture)
	{
		ArgumentNullException.ThrowIfNull(gesture);
		return gesture.IsSet ? string.Join('+', Keys(gesture)) : "None";
	}

	private static IReadOnlyList<string> Keys(ShortcutGesture gesture) =>
		gesture.Shift ? ["Ctrl", "Shift", KeyName(gesture.Code)] : ["Ctrl", KeyName(gesture.Code)];

	private static bool IsLetter(string code) => code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal);

	/// <summary>The key as it is printed on a keyboard, because a gesture is read, not parsed.</summary>
	private static string KeyName(string code) => code switch
	{
		"Comma" => ",",
		"Period" => ".",
		"Slash" => "/",
		"Backslash" => "\\",
		"Semicolon" => ";",
		"Quote" => "'",
		"Backquote" => "`",
		"Minus" => "-",
		"Equal" => "=",
		"BracketLeft" => "[",
		"BracketRight" => "]",
		_ when code.StartsWith("Key", StringComparison.Ordinal) => code[3..],
		_ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
		_ => code,
	};
}
