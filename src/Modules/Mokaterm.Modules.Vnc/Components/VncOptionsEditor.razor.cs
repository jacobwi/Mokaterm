using System.Globalization;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Vnc.Components;

/// <summary>The VNC part of the connection editor: encryption, scaling, picture quality, sharing and view only.</summary>
public partial class VncOptionsEditor : ConnectionOptionsEditorBase<VncConnectionOptions>
{
	private static readonly string QualityHelp = string.Create(
		CultureInfo.CurrentCulture,
		$"{VncConnectionOptions.MinLevel} is blurry and small, {VncConnectionOptions.MaxLevel} is sharp and large. Only servers that use the Tight encoding act on it.");

	private static readonly string CompressionHelp = string.Create(
		CultureInfo.CurrentCulture,
		$"{VncConnectionOptions.MinLevel} costs the least processor time, {VncConnectionOptions.MaxLevel} sends the least data.");

	private const string SharedHelp = "Off asks the server to disconnect other viewers when this session opens.";

	private string EncryptionHelp => Current.Encryption == VncEncryptionMode.Required
		? "Refuses the connection unless the server offers VeNCrypt with TLS."
		: "Uses TLS when the server offers VeNCrypt and connects unencrypted otherwise. The session view says which one it got.";

	private string ScalingHelp => Current.Scaling switch
	{
		VncScalingMode.Actual => "Shows every remote pixel and scrolls when the screen is larger than the view.",
		VncScalingMode.Remote => "Asks the server to match its screen to the view. Servers that cannot resize stay at 1:1.",
		_ => "Scales the whole screen into the view, which keeps the picture complete but not pixel exact.",
	};

	protected override VncConnectionOptions From(ProtocolOptions options) => VncConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(VncConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	private Task OnEncryptionChangedAsync(string value) =>
		Enum.TryParse(value, out VncEncryptionMode encryption) && Enum.IsDefined(encryption)
			? ChangeAsync(Current with { Encryption = encryption })
			: Task.CompletedTask;

	private Task OnScalingChangedAsync(string value) =>
		Enum.TryParse(value, out VncScalingMode scaling) && Enum.IsDefined(scaling)
			? ChangeAsync(Current with { Scaling = scaling })
			: Task.CompletedTask;

	private Task OnQualityChangedAsync(int value) => ChangeAsync(Current with { Quality = Clamp(value) });

	private Task OnCompressionChangedAsync(int value) => ChangeAsync(Current with { Compression = Clamp(value) });

	private Task OnSharedChangedAsync(bool value) => ChangeAsync(Current with { Shared = value });

	private Task OnViewOnlyChangedAsync(bool value) => ChangeAsync(Current with { ViewOnly = value });

	// MokaNumericField does not apply Min and Max, so the levels are kept in range here.
	private static int Clamp(int level) => Math.Clamp(level, VncConnectionOptions.MinLevel, VncConnectionOptions.MaxLevel);
}
