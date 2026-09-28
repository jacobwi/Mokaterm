using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Telnet.Components;

/// <summary>
/// The telnet part of the connection editor: terminal type, character set, what Enter sends, who echoes, and the
/// automatic login.
/// </summary>
public partial class TelnetOptionsEditor : ConnectionOptionsEditorBase<TelnetConnectionOptions>
{
	private const string AutoLoginHelp =
		"Types the saved user name and password when the device asks for them. Off by default: telnet sends the password in clear text.";

	private readonly FieldDraft<string> _terminalType = new();

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	private string DefaultTerminalType => SettingsService.Get<TerminalSettings>().TerminalType;

	private string LineEndingHelp => Current.LineEnding switch
	{
		TelnetLineEnding.CrNul => "CR NUL: a carriage return with no line feed, which line oriented devices expect.",
		TelnetLineEnding.Lf => "LF alone, for consoles and terminal servers that treat a carriage return as a stray character.",
		_ => "CR LF, the new line of RFC 854. Almost every telnet server expects it.",
	};

	private string EchoHelp => Current.Echo switch
	{
		TelnetEchoMode.On => "Always show what is typed. For devices that never echo and never negotiate.",
		TelnetEchoMode.Off => "Never show what is typed locally. For devices that echo without negotiating.",
		_ => "Show what is typed until the server turns the ECHO option on and takes over.",
	};

	protected override TelnetConnectionOptions From(ProtocolOptions options) => TelnetConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(TelnetConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	private Task OnTerminalTypeChangedAsync(string? value)
	{
		string terminalType = (value ?? "").Trim();
		if (terminalType.Length == 0)
		{
			_terminalType.Clear();
			return ChangeAsync(Current with { TerminalType = null });
		}

		if (!TelnetConnectionOptions.IsValidTerminalType(terminalType))
		{
			_terminalType.Reject(value ?? "", "Use letters, digits, '-', '_', '.' or '+'.");
			return Task.CompletedTask;
		}

		_terminalType.Clear();
		return ChangeAsync(Current with { TerminalType = terminalType });
	}

	private Task OnEncodingChangedAsync(string value) => ChangeAsync(Current with { EncodingName = value });

	private Task OnLineEndingChangedAsync(string value) =>
		Enum.TryParse(value, out TelnetLineEnding lineEnding) && Enum.IsDefined(lineEnding)
			? ChangeAsync(Current with { LineEnding = lineEnding })
			: Task.CompletedTask;

	private Task OnEchoChangedAsync(string value) =>
		Enum.TryParse(value, out TelnetEchoMode echo) && Enum.IsDefined(echo)
			? ChangeAsync(Current with { Echo = echo })
			: Task.CompletedTask;

	private Task OnAutoLoginChangedAsync(bool value) => ChangeAsync(Current with { AutoLogin = value });
}
