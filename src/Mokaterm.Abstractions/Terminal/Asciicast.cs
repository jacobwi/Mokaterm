using System.Globalization;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// The parts of the asciicast format that the session log writer and the player both have to agree on. A file is one
/// JSON header line followed by one JSON array per event: <c>[seconds, "o", "text"]</c> for output and
/// <c>[seconds, "r", "120x32"]</c> for a resize.
/// </summary>
public static class Asciicast
{
	public const string Extension = ".cast";

	/// <summary>The version the writer produces. Version 2 gives every event its time since the start.</summary>
	public const int Version = 2;

	/// <summary>Version 3, asciinema 3's default, which gives every event its time since the one before.</summary>
	public const int IntervalVersion = 3;

	public const string OutputCode = "o";

	public const string ResizeCode = "r";

	/// <summary><c>COLSxROWS</c>, the payload of a resize event.</summary>
	public static string FormatSize(int columns, int rows) =>
		string.Create(CultureInfo.InvariantCulture, $"{columns}x{rows}");

	/// <summary>Reads a resize payload. False for anything that is not two positive whole numbers around an <c>x</c>.</summary>
	public static bool TryParseSize(string? value, out int columns, out int rows)
	{
		columns = 0;
		rows = 0;
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		int separator = value.IndexOf('x', StringComparison.Ordinal);
		if (separator > 0
			&& int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedColumns)
			&& int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRows)
			&& parsedColumns > 0
			&& parsedRows > 0)
		{
			columns = parsedColumns;
			rows = parsedRows;
			return true;
		}

		return false;
	}
}
