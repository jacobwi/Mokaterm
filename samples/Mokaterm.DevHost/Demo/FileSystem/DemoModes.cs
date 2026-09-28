using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>Unix modes written the way <c>chmod</c> takes them.</summary>
internal static class DemoModes
{
	/// <summary>What <c>mkdir</c> creates under the usual umask of 022.</summary>
	public static readonly UnixFileMode NewDirectory = Octal("755");

	/// <summary>What a new file gets under the usual umask of 022.</summary>
	public static readonly UnixFileMode NewFile = Octal("644");

	/// <summary>Symbolic links always show every bit; their target's mode is what counts.</summary>
	public static readonly UnixFileMode Link = Octal("777");

	public static UnixFileMode Octal(string digits) =>
		UnixFileModeFormat.TryParseOctal(digits, out UnixFileMode mode)
			? mode
			: throw new ArgumentException($"'{digits}' is not an octal file mode.", nameof(digits));
}
