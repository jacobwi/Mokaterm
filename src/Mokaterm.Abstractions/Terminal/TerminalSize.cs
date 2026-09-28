namespace Mokaterm.Abstractions.Terminal;

/// <summary>Terminal grid size in character cells, with optional pixel dimensions for protocols that send them.</summary>
public readonly record struct TerminalSize(int Columns, int Rows, int PixelWidth = 0, int PixelHeight = 0)
{
	public static TerminalSize Default { get; } = new(120, 32);

	public bool IsValid => Columns > 0 && Rows > 0;
}
