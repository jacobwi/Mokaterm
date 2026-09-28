namespace Mokaterm.Abstractions.Terminal;

/// <summary>Color scheme formats <see cref="ITerminalThemeConverter"/> understands.</summary>
public enum TerminalThemeFormat
{
	/// <summary>A Windows Terminal color scheme object (JSON), alone or inside a settings file's "schemes" array.</summary>
	WindowsTerminal,

	/// <summary>An iTerm2 .itermcolors property list.</summary>
	ITerm2,

	/// <summary>An Alacritty colors table, in TOML or YAML.</summary>
	Alacritty,

	/// <summary>X resources: *.foreground, *.background and *.color0 to *.color15.</summary>
	Xresources,

	/// <summary>VS Code "workbench.colorCustomizations" terminal keys (JSON).</summary>
	VsCode,
}
