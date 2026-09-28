namespace Mokaterm.Abstractions.FileSystem;

/// <summary>Formats and parses <see cref="UnixFileMode"/> the way <c>ls</c> and <c>chmod</c> do.</summary>
public static class UnixFileModeFormat
{
	private const UnixFileMode AllBits = (UnixFileMode)0xFFF;

	/// <summary><c>755</c>, or <c>4755</c> when setuid, setgid or sticky bits are set.</summary>
	public static string ToOctal(UnixFileMode mode)
	{
		int value = (int)(mode & AllBits);
		return Convert.ToString(value, 8).PadLeft(value > 0x1FF ? 4 : 3, '0');
	}

	/// <summary><c>rwxr-xr-x</c>, with <c>s</c>/<c>S</c> and <c>t</c>/<c>T</c> for the special bits.</summary>
	public static string ToSymbolic(UnixFileMode mode) => string.Create(9, mode, static (span, m) =>
	{
		span[0] = Has(m, UnixFileMode.UserRead) ? 'r' : '-';
		span[1] = Has(m, UnixFileMode.UserWrite) ? 'w' : '-';
		span[2] = Special(Has(m, UnixFileMode.UserExecute), Has(m, UnixFileMode.SetUser), 's');
		span[3] = Has(m, UnixFileMode.GroupRead) ? 'r' : '-';
		span[4] = Has(m, UnixFileMode.GroupWrite) ? 'w' : '-';
		span[5] = Special(Has(m, UnixFileMode.GroupExecute), Has(m, UnixFileMode.SetGroup), 's');
		span[6] = Has(m, UnixFileMode.OtherRead) ? 'r' : '-';
		span[7] = Has(m, UnixFileMode.OtherWrite) ? 'w' : '-';
		span[8] = Special(Has(m, UnixFileMode.OtherExecute), Has(m, UnixFileMode.StickyBit), 't');
	});

	/// <summary>Parses 3 or 4 octal digits such as <c>644</c> or <c>2775</c>.</summary>
	public static bool TryParseOctal(string? text, out UnixFileMode mode)
	{
		mode = UnixFileMode.None;
		string trimmed = text?.Trim() ?? "";
		if (trimmed.Length is < 3 or > 4 || trimmed.Any(c => c is < '0' or > '7'))
		{
			return false;
		}

		mode = (UnixFileMode)Convert.ToInt32(trimmed, 8);
		return true;
	}

	private static bool Has(UnixFileMode mode, UnixFileMode flag) => (mode & flag) == flag;

	private static char Special(bool execute, bool special, char letter) =>
		(execute, special) switch
		{
			(true, true) => letter,
			(false, true) => char.ToUpperInvariant(letter),
			(true, false) => 'x',
			_ => '-',
		};
}
