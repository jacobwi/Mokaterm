using System.Runtime.InteropServices;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Local.Pty.Windows;

/// <summary>COORD: a console size in character cells, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Coord(short X, short Y)
{
	/// <summary>The size a console can take: at least one cell each way, and no more than a short holds.</summary>
	public static Coord From(TerminalSize size) =>
		new((short)Math.Clamp(size.Columns, 1, short.MaxValue), (short)Math.Clamp(size.Rows, 1, short.MaxValue));
}
