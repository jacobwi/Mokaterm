using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Ports;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// The port names this machine has, and the rules a typed one has to satisfy. The rules are the driver's, not
/// ours: on Windows <c>System.IO.Ports</c> refuses anything that does not start with COM and builds the
/// <c>\\.\COM12</c> form itself, so a name pasted in that form is unwrapped here instead of being rejected.
/// </summary>
internal static class SerialPortNames
{
	/// <summary>Longer than any real device path, and short enough that a pasted mistake is caught.</summary>
	public const int MaxLength = 128;

	private const string WindowsDevicePrefix = @"\\.\";

	/// <summary>The ports this machine has right now, in a natural order, or empty when there are none.</summary>
	public static IReadOnlyList<string> List()
	{
		if (!SerialPlatform.IsSupported)
		{
			return [];
		}

		string[] names;
		try
		{
			names = SerialPort.GetPortNames();
		}
		catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			// A machine with no serial support at all, or a registry the user may not read: no ports is the answer.
			return [];
		}

		// COM10 belongs after COM9, which an ordinal sort would not do.
		return [.. names
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(SortPrefix, StringComparer.OrdinalIgnoreCase)
			.ThenBy(SortNumber)
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>The name as the driver wants it: trimmed, and without the <c>\\.\</c> prefix .NET adds itself.</summary>
	public static string Normalize(string? name)
	{
		string trimmed = name?.Trim() ?? "";
		return trimmed.StartsWith(WindowsDevicePrefix, StringComparison.Ordinal)
			? trimmed[WindowsDevicePrefix.Length..].Trim()
			: trimmed;
	}

	/// <summary>A message naming what is wrong with <paramref name="name"/>, or null when it can be opened.</summary>
	public static string? Validate(string? name) => Validate(name, OperatingSystem.IsWindows());

	/// <summary>
	/// The rule with the platform passed in, so both halves of it can be tested on either kind of machine.
	/// </summary>
	public static string? Validate(string? name, bool windows)
	{
		string port = Normalize(name);
		if (port.Length == 0)
		{
			return "Name the serial port, for example COM3 or /dev/ttyUSB0.";
		}

		if (port.Length > MaxLength)
		{
			return $"A port name is at most {MaxLength} characters.";
		}

		if (port.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
		{
			return "A port name has no spaces in it.";
		}

		// System.IO.Ports itself refuses everything else on Windows, with a message that names no way forward.
		return windows && !IsWindowsPortName(port)
			? "On Windows a port name is COM followed by its number, for example COM3."
			: null;
	}

	public static bool IsValid([NotNullWhen(true)] string? name) => name is not null && Validate(name) is null;

	private static bool IsWindowsPortName(string port) =>
		port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
		&& port.Length > 3
		&& port.AsSpan(3).ToString().All(char.IsAsciiDigit);

	private static string SortPrefix(string name)
	{
		int end = name.Length;
		while (end > 0 && char.IsAsciiDigit(name[end - 1]))
		{
			end--;
		}

		return name[..end];
	}

	private static int SortNumber(string name)
	{
		int start = name.Length;
		while (start > 0 && char.IsAsciiDigit(name[start - 1]))
		{
			start--;
		}

		return int.TryParse(name.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : -1;
	}
}
