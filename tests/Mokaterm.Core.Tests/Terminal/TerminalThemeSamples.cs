namespace Mokaterm.Core.Tests.Terminal;

/// <summary>Color schemes as their projects publish them, used as they were downloaded.</summary>
internal static class TerminalThemeSamples
{
	/// <summary>Dracula as Windows Terminal publishes it (iTerm2-Color-Schemes, windowsterminal/Dracula.json).</summary>
	public const string WindowsTerminalDracula =
		"""
		{
		  "name": "Dracula",
		  "black": "#21222c",
		  "red": "#ff5555",
		  "green": "#50fa7b",
		  "yellow": "#f1fa8c",
		  "blue": "#bd93f9",
		  "purple": "#ff79c6",
		  "cyan": "#8be9fd",
		  "white": "#f8f8f2",
		  "brightBlack": "#6272a4",
		  "brightRed": "#ff6e6e",
		  "brightGreen": "#69ff94",
		  "brightYellow": "#ffffa5",
		  "brightBlue": "#d6acff",
		  "brightPurple": "#ff92df",
		  "brightCyan": "#a4ffff",
		  "brightWhite": "#ffffff",
		  "background": "#282a36",
		  "foreground": "#f8f8f2",
		  "cursorColor": "#f8f8f2",
		  "selectionBackground": "#44475a"
		}
		""";

	/// <summary>Dracula as an iTerm2 property list (iTerm2-Color-Schemes, schemes/Dracula.itermcolors).</summary>
	public const string ITerm2Dracula =
		"""
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		<plist version="1.0">
		<dict>
			<key>Ansi 0 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.1725</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.1333</real>
				<key>Red Component</key>
				<real>0.1294</real>
			</dict>
			<key>Ansi 1 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.3333</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.3333</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Ansi 10 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.5804</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>1.0</real>
				<key>Red Component</key>
				<real>0.4118</real>
			</dict>
			<key>Ansi 11 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.6471</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>1.0</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Ansi 12 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>1.0</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.6745</real>
				<key>Red Component</key>
				<real>0.8392</real>
			</dict>
			<key>Ansi 13 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.8745</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.5725</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Ansi 14 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>1.0</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>1.0</real>
				<key>Red Component</key>
				<real>0.6431</real>
			</dict>
			<key>Ansi 15 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>1.0</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>1.0</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Ansi 2 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.4824</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9804</real>
				<key>Red Component</key>
				<real>0.3137</real>
			</dict>
			<key>Ansi 3 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.549</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9804</real>
				<key>Red Component</key>
				<real>0.9451</real>
			</dict>
			<key>Ansi 4 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.9765</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.5765</real>
				<key>Red Component</key>
				<real>0.7412</real>
			</dict>
			<key>Ansi 5 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.7765</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.4745</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Ansi 6 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.9922</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9137</real>
				<key>Red Component</key>
				<real>0.5451</real>
			</dict>
			<key>Ansi 7 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.949</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9725</real>
				<key>Red Component</key>
				<real>0.9725</real>
			</dict>
			<key>Ansi 8 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.6431</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.4471</real>
				<key>Red Component</key>
				<real>0.3843</real>
			</dict>
			<key>Ansi 9 Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.4314</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.4314</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Background Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.2118</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.1647</real>
				<key>Red Component</key>
				<real>0.1569</real>
			</dict>
			<key>Bold Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.949</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9725</real>
				<key>Red Component</key>
				<real>0.9725</real>
			</dict>
			<key>Cursor Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.949</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9725</real>
				<key>Red Component</key>
				<real>0.9725</real>
			</dict>
			<key>Cursor Guide Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.949</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9725</real>
				<key>Red Component</key>
				<real>0.9725</real>
			</dict>
			<key>Cursor Text Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.2118</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.1647</real>
				<key>Red Component</key>
				<real>0.1569</real>
			</dict>
			<key>Foreground Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.949</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.9725</real>
				<key>Red Component</key>
				<real>0.9725</real>
			</dict>
			<key>Selected Text Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>1.0</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>1.0</real>
				<key>Red Component</key>
				<real>1.0</real>
			</dict>
			<key>Selection Color</key>
			<dict>
				<key>Alpha Component</key>
				<real>1</real>
				<key>Blue Component</key>
				<real>0.3529</real>
				<key>Color Space</key>
				<string>sRGB</string>
				<key>Green Component</key>
				<real>0.2784</real>
				<key>Red Component</key>
				<real>0.2667</real>
			</dict>
		</dict>
		</plist>
		""";

	/// <summary>Dracula for Alacritty 0.13 and later (iTerm2-Color-Schemes, alacritty/Dracula.toml).</summary>
	public const string AlacrittyTomlDracula =
		"""
		# Colors (Dracula)

		[colors.bright]
		black = '#6272a4'
		blue = '#d6acff'
		cyan = '#a4ffff'
		green = '#69ff94'
		magenta = '#ff92df'
		red = '#ff6e6e'
		white = '#ffffff'
		yellow = '#ffffa5'

		[colors.cursor]
		cursor = '#f8f8f2'
		text = '#282a36'

		[colors.normal]
		black = '#21222c'
		blue = '#bd93f9'
		cyan = '#8be9fd'
		green = '#50fa7b'
		magenta = '#ff79c6'
		red = '#ff5555'
		white = '#f8f8f2'
		yellow = '#f1fa8c'

		[colors.primary]
		background = '#282a36'
		foreground = '#f8f8f2'

		[colors.selection]
		background = '#44475a'
		text = '#ffffff'
		""";

	/// <summary>Gruvbox dark for Alacritty, whose comments mention colors the theme does not use (alacritty/alacritty-theme, themes/gruvbox_dark.toml).</summary>
	public const string AlacrittyTomlGruvbox =
		"""
		# Colors (Gruvbox dark)

		# Default colors
		[colors.primary]
		# hard contrast background = = '#1d2021'
		background = '#282828'
		# soft contrast background = = '#32302f'
		foreground = '#ebdbb2'

		# Normal colors
		[colors.normal]
		black   = '#282828'
		red     = '#cc241d'
		green   = '#98971a'
		yellow  = '#d79921'
		blue    = '#458588'
		magenta = '#b16286'
		cyan    = '#689d6a'
		white   = '#a89984'

		# Bright colors
		[colors.bright]
		black   = '#928374'
		red     = '#fb4934'
		green   = '#b8bb26'
		yellow  = '#fabd2f'
		blue    = '#83a598'
		magenta = '#d3869b'
		cyan    = '#8ec07c'
		white   = '#ebdbb2'
		""";

	/// <summary>Gruvbox dark in the old Alacritty YAML layout (alacritty/alacritty-theme, themes/gruvbox_dark.yaml before the TOML move).</summary>
	public const string AlacrittyYamlGruvbox =
		"""
		# Colors (Gruvbox dark)
		colors:
		  # Default colors
		  primary:
		    # hard contrast: background = '0x1d2021'
		    background: '0x282828'
		    # soft contrast: background = '0x32302f'
		    foreground: '0xebdbb2'

		  # Normal colors
		  normal:
		    black:   '0x282828'
		    red:     '0xcc241d'
		    green:   '0x98971a'
		    yellow:  '0xd79921'
		    blue:    '0x458588'
		    magenta: '0xb16286'
		    cyan:    '0x689d6a'
		    white:   '0xa89984'

		  # Bright colors
		  bright:
		    black:   '0x928374'
		    red:     '0xfb4934'
		    green:   '0xb8bb26'
		    yellow:  '0xfabd2f'
		    blue:    '0x83a598'
		    magenta: '0xd3869b'
		    cyan:    '0x8ec07c'
		    white:   '0xebdbb2'
		""";

	/// <summary>Dracula as X resources (iTerm2-Color-Schemes, Xresources/Dracula).</summary>
	public const string XresourcesDracula =
		"""
		!
		! Dracula
		!
		*.foreground:  #f8f8f2
		*.background:  #282a36
		*.cursorColor: #f8f8f2
		!
		! Black
		*.color0:      #21222c
		*.color8:      #6272a4
		!
		! Red
		*.color1:      #ff5555
		*.color9:      #ff6e6e
		!
		! Green
		*.color2:      #50fa7b
		*.color10:     #69ff94
		!
		! Yellow
		*.color3:      #f1fa8c
		*.color11:     #ffffa5
		!
		! Blue
		*.color4:      #bd93f9
		*.color12:     #d6acff
		!
		! Magenta
		*.color5:      #ff79c6
		*.color13:     #ff92df
		!
		! Cyan
		*.color6:      #8be9fd
		*.color14:     #a4ffff
		!
		! White
		*.color7:      #f8f8f2
		*.color15:     #ffffff
		!
		! Bold, Italic, Underline
		*.colorBD:     #f8f8f2
		!*.colorIT:
		!*.colorUL:
		""";

	/// <summary>The base16 X resources template, which names its colors with #define (tinted-theming/tinted-xresources, base16-gruvbox-dark-hard).</summary>
	public const string XresourcesBase16Gruvbox =
		"""
		! base16 Gruvbox dark, hard
		! Scheme author: Dawid Kurek (dawikur@gmail.com), morhetz (https://github.com/morhetz/gruvbox)
		! Template author: Tinted Theming (https://github.com/tinted-theming)

		#define base00 #1d2021
		#define base01 #3c3836
		#define base02 #504945
		#define base03 #665c54
		#define base04 #bdae93
		#define base05 #d5c4a1
		#define base06 #ebdbb2
		#define base07 #fbf1c7
		#define base08 #fb4934
		#define base09 #fe8019
		#define base0A #fabd2f
		#define base0B #b8bb26
		#define base0C #8ec07c
		#define base0D #83a598
		#define base0E #d3869b
		#define base0F #d65d0e

		*foreground:   base05
		#ifdef background_opacity
		*background:   [background_opacity]base00
		#else
		*background:   base00
		#endif
		*cursorColor:  base05

		*color0:       base00
		*color1:       base08
		*color2:       base0B
		*color3:       base0A
		*color4:       base0D
		*color5:       base0E
		*color6:       base0C
		*color7:       base05

		*color8:       base03
		*color9:       base08
		*color10:      base0B
		*color11:      base0A
		*color12:      base0D
		*color13:      base0E
		*color14:      base0C
		*color15:      base07

		! Note: colors beyond 15 might not be loaded (e.g., xterm, urxvt),
		! use 'shell' template to set these if necessary
		*color16:      base09
		*color17:      base0F
		*color18:      base01
		*color19:      base02
		*color20:      base04
		*color21:      base06
		""";

	/// <summary>Dracula as VS Code terminal customizations (iTerm2-Color-Schemes, vscode/Dracula.json).</summary>
	public const string VsCodeDracula =
		"""
		{
		    "workbench.colorCustomizations": {
		        "terminal.foreground": "#f8f8f2",
		        "terminal.background": "#282a36",
		        "terminal.ansiBlack": "#21222c",
		        "terminal.ansiBlue": "#bd93f9",
		        "terminal.ansiCyan": "#8be9fd",
		        "terminal.ansiGreen": "#50fa7b",
		        "terminal.ansiMagenta": "#ff79c6",
		        "terminal.ansiRed": "#ff5555",
		        "terminal.ansiWhite": "#f8f8f2",
		        "terminal.ansiYellow": "#f1fa8c",
		        "terminal.ansiBrightBlack": "#6272a4",
		        "terminal.ansiBrightBlue": "#d6acff",
		        "terminal.ansiBrightCyan": "#a4ffff",
		        "terminal.ansiBrightGreen": "#69ff94",
		        "terminal.ansiBrightMagenta": "#ff92df",
		        "terminal.ansiBrightRed": "#ff6e6e",
		        "terminal.ansiBrightWhite": "#ffffff",
		        "terminal.ansiBrightYellow": "#ffffa5",
		        "terminal.selectionBackground": "#44475a",
		        "terminalCursor.background": "#282a36",
		        "terminalCursor.foreground": "#f8f8f2"
		    }
		}
		""";
}
