using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Terminal;

/// <summary>
/// The colors one reader found, in the order xterm uses (0 black to 15 bright white). <see cref="Build"/> fills the
/// gaps, names the theme and gives it an id.
/// </summary>
internal sealed class TerminalThemeDraft
{
	public const int AnsiCount = 16;

	private const int MaxNameLength = 64;

	private readonly string?[] _ansi = new string?[AnsiCount];
	private readonly TerminalThemeFormat _format;
	private readonly string _formatLabel;
	private readonly IReadOnlyList<string> _slotNames;
	private string? _name;

	/// <param name="formatLabel">How the format is named in a failure message, such as "an iTerm2 file".</param>
	/// <param name="slotNames">The 16 color keys as that format spells them, used in failure messages.</param>
	public TerminalThemeDraft(TerminalThemeFormat format, string formatLabel, IReadOnlyList<string> slotNames)
	{
		_format = format;
		_formatLabel = formatLabel;
		_slotNames = slotNames;
	}

	public string? Background { get; private set; }

	public string? Foreground { get; private set; }

	public string? Cursor { get; private set; }

	public string? CursorAccent { get; private set; }

	public string? SelectionBackground { get; private set; }

	public string? SelectionForeground { get; private set; }

	/// <summary>True once any color was read, so a caller can tell an unrelated file from an incomplete scheme.</summary>
	public bool HasColor =>
		Background is not null || Foreground is not null || Cursor is not null || Array.Exists(_ansi, color => color is not null);

	public void SetName(string? value) => _name = value;

	public void SetBackground(string? value) => Background = TerminalColors.ParseOpaque(value) ?? Background;

	public void SetForeground(string? value) => Foreground = TerminalColors.ParseOpaque(value) ?? Foreground;

	public void SetCursor(string? value) => Cursor = TerminalColors.ParseOpaque(value) ?? Cursor;

	public void SetCursorAccent(string? value) => CursorAccent = TerminalColors.ParseOpaque(value) ?? CursorAccent;

	/// <summary>The one slot that keeps alpha, so a translucent selection survives the formats that can carry it.</summary>
	public void SetSelectionBackground(string? value) => SelectionBackground = TerminalColors.Parse(value) ?? SelectionBackground;

	public void SetSelectionForeground(string? value) => SelectionForeground = TerminalColors.ParseOpaque(value) ?? SelectionForeground;

	public void SetAnsi(int index, string? value)
	{
		if (TerminalColors.ParseOpaque(value) is { } color)
		{
			_ansi[index] = color;
		}
	}

	/// <summary>The theme, or a failure naming the first color the format left out.</summary>
	public TerminalThemeImport Build(string? fileName)
	{
		if (!HasColor)
		{
			return TerminalThemeImport.Failure($"There are no colors here to read as {_formatLabel}.");
		}

		string[] ansi = new string[AnsiCount];
		for (int index = 0; index < 8; index++)
		{
			if (_ansi[index] is not { } color)
			{
				return TerminalThemeImport.Failure($"This looks like {_formatLabel}, but it has no {_slotNames[index]}.");
			}

			ansi[index] = color;
		}

		for (int index = 8; index < AnsiCount; index++)
		{
			ansi[index] = _ansi[index] ?? TerminalColors.Lighten(ansi[index - 8], TerminalColors.BrightLift);
		}

		string background = Background ?? ansi[0];
		string foreground = Foreground ?? ansi[7];
		TerminalTheme theme = new()
		{
			Id = NewId(),
			Name = Name(fileName),
			IsCustom = true,
			IsDark = TerminalColors.IsDark(background),
			Background = background,
			Foreground = foreground,
			Cursor = Cursor ?? foreground,
			CursorAccent = CursorAccent ?? background,
			SelectionBackground = SelectionBackground ?? TerminalColors.WithAlpha(foreground, TerminalColors.SelectionAlpha),
			SelectionForeground = SelectionForeground,
			Black = ansi[0],
			Red = ansi[1],
			Green = ansi[2],
			Yellow = ansi[3],
			Blue = ansi[4],
			Magenta = ansi[5],
			Cyan = ansi[6],
			White = ansi[7],
			BrightBlack = ansi[8],
			BrightRed = ansi[9],
			BrightGreen = ansi[10],
			BrightYellow = ansi[11],
			BrightBlue = ansi[12],
			BrightMagenta = ansi[13],
			BrightCyan = ansi[14],
			BrightWhite = ansi[15],
		};

		return TerminalThemeRules.IsComplete(theme)
			? TerminalThemeImport.Success(theme, _format)
			: TerminalThemeImport.Failure($"This looks like {_formatLabel}, but too much of it is missing.");
	}

	/// <summary>Trims a name down to one printable line.</summary>
	private static string? CleanName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		StringBuilder cleaned = new(Math.Min(value.Length, MaxNameLength));
		bool space = false;
		foreach (char character in value)
		{
			if (char.IsWhiteSpace(character) || char.IsControl(character))
			{
				space = cleaned.Length > 0;
				continue;
			}

			if (space && cleaned.Length < MaxNameLength)
			{
				cleaned.Append(' ');
			}

			space = false;
			if (cleaned.Length >= MaxNameLength)
			{
				break;
			}

			cleaned.Append(character);
		}

		return cleaned.Length == 0 ? null : cleaned.ToString();
	}

	private static string NewId() =>
		"custom-" + BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)).ToString("x8", CultureInfo.InvariantCulture);

	private string Name(string? fileName)
	{
		string? stem = fileName is null ? null : Path.GetFileNameWithoutExtension(fileName.AsSpan()).ToString();
		return CleanName(_name) ?? CleanName(stem) ?? "Imported Theme";
	}
}
