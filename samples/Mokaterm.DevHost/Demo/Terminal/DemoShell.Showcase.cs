using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Terminal;
using static System.FormattableString;

namespace Mokaterm.DevHost.Demo.Terminal;

// Commands that exist to exercise the terminal view: help, echo, seq, colors, unicode, top, lorem and stty.
internal sealed partial class DemoShell
{
	private const int DefaultSequenceLength = 2000;
	private const int StateColumn = 7;
	private const int CpuColumn = 8;
	private const string SelectedRowColor = "\u001b[30;46m";

	private static readonly string[] ColorNames = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];

	private static readonly (string Command, string Description)[] Commands =
	[
		("help", "this list"),
		("ls [-lah] [PATH...]", "list files; folders, links and executables in color"),
		("cd [DIR], pwd", "change or print the working folder"),
		("cat FILE...", "print files"),
		("mkdir [-p] DIR...", "create folders"),
		("touch FILE...", "create empty files or update their time"),
		("rm [-rf] PATH...", "delete files and folders"),
		("echo [-ne] TEXT", "print text; -e reads \\e, \\n, \\t and \\xHH"),
		("whoami, history", "who you are, what you typed"),
		("clear, Ctrl+L", "clear the screen"),
		("seq [FIRST] [LAST]", "count lines, 2000 by default, to fill the scrollback"),
		("colors", "16 colors, text styles, the 256-color cube and 24-bit gradients"),
		("unicode", "box drawing, blocks, CJK, emoji, combining marks, right to left"),
		("top", "an htop-style screen"),
		("tail [-n N] [-f] FILE", "last lines; -f streams log lines until Ctrl+C"),
		("lorem [N]", "N paragraphs of wrapped text"),
		("stty size", "rows and columns the terminal reported"),
		("sudo -s, sudo CMD", "a root shell, or one command as root"),
		("exit, Ctrl+D", "leave the root shell, or end the session"),
	];

	private static readonly string[] LoremSentences =
	[
		"Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
		"Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
		"Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.",
		"Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.",
		"Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.",
		"Curabitur pretium tincidunt lacus, nulla gravida orci a odio.",
		"Nullam varius, turpis et commodo pharetra, est eros bibendum elit, nec luctus magna felis sollicitudin mauris.",
		"Integer in mauris eu nibh euismod gravida.",
		"Duis ac tellus et risus vulputate vehicula.",
		"Etiam tempor, ut ullamcorper, ligula eu tempor congue, eros est euismod turpis, id tincidunt sapien risus a quam.",
		"Maecenas fermentum consequat mi, donec fermentum.",
		"Pellentesque malesuada nulla a mi.",
	];

	// PID, user, priority, nice, virtual, resident, shared, state, CPU%, MEM%, time and command; {user} is the login.
	private static readonly string[][] Processes =
	[
		["1", "root", "20", "0", "167M", "13044", "8396", "S", "0.0", "0.2", "0:08.12", "/sbin/init splash"],
		["412", "root", "19", "-1", "48316", "15632", "14592", "S", "0.0", "0.2", "0:02.31", "/lib/systemd/systemd-journald"],
		["612", "systemd-r", "20", "0", "25532", "13412", "10548", "S", "0.0", "0.2", "0:00.87", "/lib/systemd/systemd-resolved"],
		["788", "syslog", "20", "0", "222M", "6112", "4384", "S", "0.0", "0.1", "0:00.44", "/usr/sbin/rsyslogd -n -iNONE"],
		["1123", "www-data", "20", "0", "56892", "8124", "5632", "S", "3.9", "0.1", "1:02.44", "nginx: worker process"],
		["1187", "postgres", "20", "0", "215M", "28900", "26044", "S", "1.3", "0.4", "0:41.07", "postgres: checkpointer"],
		["1874", "root", "20", "0", "15436", "9132", "7676", "S", "0.0", "0.1", "0:00.12", "sshd: /usr/sbin/sshd -D [listener] 0 of 10-100 startups"],
		["2210", "root", "20", "0", "1.8G", "74540", "39212", "S", "12.6", "0.9", "4:12.88", "/usr/bin/dockerd -H fd:// --containerd=/run/containerd/containerd.sock"],
		["2718", "{user}", "20", "0", "1.2G", "312M", "41220", "R", "48.2", "3.9", "18:03.51", "dotnet Mokaterm.Agent.dll --urls http://0.0.0.0:5080"],
		["2731", "{user}", "20", "0", "1.2G", "312M", "41220", "S", "6.1", "3.9", "2:44.10", "dotnet Mokaterm.Agent.dll --urls http://0.0.0.0:5080"],
		["3310", "root", "20", "0", "987M", "42136", "21880", "S", "0.7", "0.5", "0:52.39", "/usr/bin/containerd"],
		["21874", "{user}", "20", "0", "8784", "5412", "3524", "S", "0.0", "0.1", "0:00.02", "-bash"],
		["21990", "{user}", "20", "0", "10352", "4028", "3264", "R", "0.7", "0.1", "0:00.01", "top"],
	];

	// Widths of every column but the command; negative widths align left.
	private static readonly int[] ProcessColumnWidths = [7, -9, 3, 3, 5, 5, 5, 1, 5, 4, 8];

	private static readonly (string Key, string Label)[] FunctionKeys =
	[
		("F1", "Help  "), ("F2", "Setup "), ("F3", "Search"), ("F4", "Filter"), ("F5", "Tree  "),
		("F6", "SortBy"), ("F7", "Nice -"), ("F8", "Nice +"), ("F9", "Kill  "), ("F10", "Quit"),
	];

	private static void Help(ShellOutput output)
	{
		output.WriteLine($"{Ansi.Bold}Mokaterm demo shell{Ansi.Reset}: an in-memory machine for trying the terminal and file browser.");
		output.WriteLine();
		foreach ((string command, string description) in Commands)
		{
			output.WriteLine($"  {Ansi.BoldGreen}{command.PadRight(22)}{Ansi.Reset}{description}");
		}

		output.WriteLine();
		output.WriteLine("Up and Down recall history, Ctrl+C stops a command, Tab rings the bell. Files are shared with the file browser.");
	}

	private static void Echo(string[] args, ShellOutput output)
	{
		bool newline = true;
		bool escapes = false;
		int first = 0;
		for (; first < args.Length && IsEchoOptions(args[first]); first++)
		{
			foreach (char option in args[first].AsSpan(1))
			{
				newline &= option != 'n';
				escapes = option == 'e' || (escapes && option != 'E');
			}
		}

		string text = string.Join(' ', args[first..]);
		output.Write(escapes ? Unescape(text) : text);
		if (newline)
		{
			output.WriteLine();
		}
	}

	private static bool IsEchoOptions(string arg) => arg.Length > 1 && arg[0] == '-' && !arg.AsSpan(1).ContainsAnyExcept("neE");

	/// <summary>The escapes <c>echo -e</c> understands, with <c>\e</c> and <c>\xHH</c> for trying control sequences by hand.</summary>
	private static string Unescape(string text)
	{
		StringBuilder result = new(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != '\\' || i + 1 == text.Length)
			{
				result.Append(text[i]);
				continue;
			}

			char escape = text[++i];
			switch (escape)
			{
				case 'a':
					result.Append('\a');
					break;
				case 'b':
					result.Append('\b');
					break;
				case 'e' or 'E':
					result.Append('\u001b');
					break;
				case 'n':
					result.Append("\r\n");
					break;
				case 'r':
					result.Append('\r');
					break;
				case 't':
					result.Append('\t');
					break;
				case '\\':
					result.Append('\\');
					break;
				case 'x' when TryReadHex(text, i + 1, 2, out int value, out int digits):
					result.Append((char)value);
					i += digits;
					break;
				case 'u' when TryReadHex(text, i + 1, 4, out int value, out int digits):
					result.Append((char)value);
					i += digits;
					break;
				default:
					result.Append('\\').Append(escape);
					break;
			}
		}

		return result.ToString();
	}

	private static bool TryReadHex(string text, int start, int maxDigits, out int value, out int digits)
	{
		value = 0;
		digits = 0;
		while (digits < maxDigits && start + digits < text.Length && char.IsAsciiHexDigit(text[start + digits]))
		{
			char c = text[start + digits];
			value = (value * 16) + (char.IsAsciiDigit(c) ? c - '0' : char.ToLowerInvariant(c) - 'a' + 10);
			digits++;
		}

		return digits > 0;
	}

	private static async Task SequenceAsync(string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		long first = 1;
		long last = DefaultSequenceLength;
		bool valid = args switch
		{
			[] => true,
			[string end] => TryParseNumber(end, out last),
			[string start, string end] => TryParseNumber(start, out first) && TryParseNumber(end, out last),
			_ => false,
		};

		if (!valid)
		{
			output.WriteLine("usage: seq [FIRST] [LAST]");
			return;
		}

		for (long number = first; number <= last; number++)
		{
			output.WriteLine(number.ToString(CultureInfo.InvariantCulture));
			await output.FlushIfFullAsync(cancellationToken);
		}
	}

	private static bool TryParseNumber(string text, out long value) =>
		long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

	private void Colors(ShellOutput output)
	{
		StringBuilder line = new();
		output.WriteLine($"{Ansi.Bold}16 colors{Ansi.Reset}");
		for (int bright = 0; bright < 2; bright++)
		{
			// SGR 30-37 and 40-47 are the normal colors, 90-97 and 100-107 the bright ones.
			int foreground = bright == 0 ? 30 : 90;
			int background = bright == 0 ? 40 : 100;
			line.Clear().Append(bright == 0 ? "  normal " : "  bright ");
			for (int color = 0; color < ColorNames.Length; color++)
			{
				line.Append(CultureInfo.InvariantCulture, $"\u001b[{foreground + color}m{ColorNames[color],-8}");
			}

			output.WriteLine(line.Append(Ansi.Reset).ToString());
			line.Clear().Append("         ");
			for (int color = 0; color < ColorNames.Length; color++)
			{
				line.Append(CultureInfo.InvariantCulture, $"\u001b[{background + color}m       {Ansi.Reset} ");
			}

			output.WriteLine(line.ToString());
		}

		output.WriteLine();
		output.WriteLine($"{Ansi.Bold}Text styles{Ansi.Reset}");
		output.WriteLine(
			"  \u001b[1mbold\u001b[0m  \u001b[2mdim\u001b[0m  \u001b[3mitalic\u001b[0m  \u001b[4munderline\u001b[0m  " +
			"\u001b[21mdouble\u001b[0m  \u001b[4:3;58;5;203mcurly\u001b[0m  \u001b[53moverline\u001b[0m  \u001b[9mstrikethrough\u001b[0m  " +
			"\u001b[5mblink\u001b[0m  \u001b[7minverse\u001b[0m  [\u001b[8mhidden\u001b[0m]");

		output.WriteLine();
		output.WriteLine($"{Ansi.Bold}256 colors{Ansi.Reset}");
		line.Clear().Append("  ");
		for (int color = 0; color < 16; color++)
		{
			line.Append(CultureInfo.InvariantCulture, $"\u001b[48;5;{color}m  ");
		}

		output.WriteLine(line.Append(Ansi.Reset).ToString());
		for (int red = 0; red < 6; red++)
		{
			line.Clear().Append("  ");
			for (int green = 0; green < 6; green++)
			{
				for (int blue = 0; blue < 6; blue++)
				{
					line.Append(CultureInfo.InvariantCulture, $"\u001b[48;5;{16 + (36 * red) + (6 * green) + blue}m  ");
				}

				line.Append(Ansi.Reset).Append(green < 5 ? " " : "");
			}

			output.WriteLine(line.ToString());
		}

		line.Clear().Append("  ");
		for (int gray = 232; gray < 256; gray++)
		{
			line.Append(CultureInfo.InvariantCulture, $"\u001b[48;5;{gray}m  ");
		}

		output.WriteLine(line.Append(Ansi.Reset).ToString());

		output.WriteLine();
		output.WriteLine($"{Ansi.Bold}24-bit color{Ansi.Reset}");
		int width = Math.Clamp(Size.Columns - 4, 16, 240);
		line.Clear().Append("  ");
		for (int i = 0; i < width; i++)
		{
			(int red, int green, int blue) = Hue(360.0 * i / width);
			line.Append(CultureInfo.InvariantCulture, $"\u001b[48;2;{red};{green};{blue}m ");
		}

		output.WriteLine(line.Append(Ansi.Reset).ToString());

		// Upper half blocks set both colors of a cell, so the second ramp shows how smoothly foreground and background blend.
		line.Clear().Append("  ");
		for (int i = 0; i < width; i++)
		{
			int level = 255 * i / (width - 1);
			line.Append(CultureInfo.InvariantCulture, $"\u001b[38;2;{level};{level / 3};{255 - level};48;2;{255 - level};{level};{level / 2}m▀");
		}

		output.WriteLine(line.Append(Ansi.Reset).ToString());
	}

	private static (int Red, int Green, int Blue) Hue(double degrees)
	{
		double x = 1 - Math.Abs((degrees / 60 % 2) - 1);
		(double red, double green, double blue) = (int)(degrees / 60) switch
		{
			0 => (1d, x, 0d),
			1 => (x, 1d, 0d),
			2 => (0d, 1d, x),
			3 => (0d, x, 1d),
			4 => (x, 0d, 1d),
			_ => (1d, 0d, x),
		};

		return ((int)Math.Round(red * 255), (int)Math.Round(green * 255), (int)Math.Round(blue * 255));
	}

	private static void Unicode(ShellOutput output)
	{
		output.WriteLine($"{Ansi.Bold}Box drawing{Ansi.Reset}");
		output.WriteLine("  ┌──┬──┐  ╔══╦══╗  ╭──╮  ┏━━┳━━┓");
		output.WriteLine("  │ab│cd│  ║ab║cd║  │ok│  ┃ab┃cd┃");
		output.WriteLine("  ├──┼──┤  ╠══╬══╣  ╰──╯  ┣━━╋━━┫");
		output.WriteLine("  └──┴──┘  ╚══╩══╝        ┗━━┻━━┛");
		output.WriteLine($"{Ansi.Bold}Block elements and braille{Ansi.Reset}");
		output.WriteLine("  █▉▊▋▌▍▎▏  ▁▂▃▄▅▆▇█  ░▒▓█  ▖▗▘▙▚▛▜▝▞▟  ⠁⠃⠉⠙⠑⠋⠛⠓⠊⠚ ⣿⣶⣤⣀");
		output.WriteLine($"{Ansi.Bold}Symbols{Ansi.Reset}");
		output.WriteLine("  ← ↑ → ↓ ⇄ ✓ ✗ ★ ☆ ♠ ♥ ♦ ♣ € £ ¥ ° ± × ÷ ∞ ≈ ≠ ≤ ≥ π Ω µ");
		output.WriteLine($"{Ansi.Bold}CJK, two columns per character{Ansi.Reset}");
		output.WriteLine("  日本語のテキスト  中文字符  한국어 텍스트");
		output.WriteLine("  |abcdefghij|");
		output.WriteLine("  |日本語五文|  both bars line up when widths are right");
		output.WriteLine($"{Ansi.Bold}Emoji{Ansi.Reset}");
		output.WriteLine(
			"  \U0001F680 \u2705 \U0001F525 \U0001F427 \U0001F44D\U0001F3FD \U0001F469\u200D\U0001F4BB " +
			"\U0001F468\u200D\U0001F469\u200D\U0001F467 \U0001F3F3\uFE0F\u200D\U0001F308 \u2764\uFE0F");
		output.WriteLine($"{Ansi.Bold}Combining marks{Ansi.Reset}");
		output.WriteLine("  e\u0301 n\u0303 u\u0308 a\u030A  Z\u0351\u0334a\u0353\u0336l\u035C\u0337g\u0352\u0338o\u034C\u0335");
		output.WriteLine($"{Ansi.Bold}Right to left{Ansi.Reset}");
		output.WriteLine(
			"  \u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD   \u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645   " +
			"mixed: abc \u05E9\u05DC\u05D5\u05DD 123");
		output.WriteLine($"{Ansi.Bold}Powerline and Nerd Font glyphs{Ansi.Reset} (boxes mean the font lacks them)");
		output.WriteLine("  \uE0B0 \uE0B2 \uE0A0 \uF113 \uF17C \uE70C");
	}

	private void Top(ShellOutput output)
	{
		int columns = Math.Max(Size.Columns, 60);
		int barWidth = Math.Clamp((columns / 2) - 8, 16, 60);
		ulong seed = DemoRandom.Seed(_hostName);
		string[] summary =
		[
			Invariant($"{Ansi.Cyan}Tasks: {Ansi.Bold}{120 + DemoRandom.Next(seed, 60)}{Ansi.Reset}{Ansi.Cyan}, 418 thr; {Ansi.BoldGreen}2{Ansi.Reset}{Ansi.Cyan} running{Ansi.Reset}"),
			$"{Ansi.Cyan}Load average: {Ansi.Bold}0.42 {Ansi.Reset}{Ansi.Cyan}0.37 0.31{Ansi.Reset}",
			$"{Ansi.Cyan}Uptime: {Ansi.Bold}12 days, 04:18:33{Ansi.Reset}",
			"",
		];

		for (int cpu = 0; cpu < summary.Length; cpu++)
		{
			int user = 3 + DemoRandom.Next(seed + (ulong)cpu + 1, 60);
			int system = 1 + DemoRandom.Next(seed + (ulong)cpu + 11, 12);
			string meter = Meter(barWidth, [(user, Ansi.Green), (system, Ansi.Red)], Invariant($"{user + system:0.0}%"));
			output.WriteLine(Invariant($"  {cpu + 1,2}") + meter + "   " + summary[cpu]);
		}

		output.WriteLine("  Mem" + Meter(barWidth, [(28, Ansi.Green), (6, Ansi.Blue), (19, Ansi.Yellow)], "2.61G/7.76G"));
		output.WriteLine("  Swp" + Meter(barWidth, [(2, Ansi.Red)], "12.0M/2.00G"));
		output.WriteLine();

		int commandWidth = Math.Max(0, columns - ProcessColumnWidths.Sum(width => Math.Abs(width) + 1));
		string header = FormatProcess(["PID", "USER", "PRI", "NI", "VIRT", "RES", "SHR", "S", "CPU%", "MEM%", "TIME+"], highlight: false);
		output.WriteLine(Ansi.Inverse + header + Fit("Command", commandWidth) + Ansi.Reset);
		for (int row = 0; row < Processes.Length; row++)
		{
			string[] cells = [.. Processes[row].Select(cell => cell == "{user}" ? _login.Name : cell)];
			string command = Fit(cells[^1], commandWidth);

			// htop starts with its cursor on the first row.
			output.WriteLine(row == 0
				? SelectedRowColor + FormatProcess(cells, highlight: false) + command + Ansi.Reset
				: FormatProcess(cells, highlight: true) + command.TrimEnd());
		}

		StringBuilder keys = new();
		foreach ((string key, string label) in FunctionKeys)
		{
			keys.Append(key).Append(SelectedRowColor).Append(label).Append(Ansi.Reset);
		}

		output.WriteLine(keys.ToString());
	}

	// An htop meter: a colored bar per part, with the label right-aligned inside the brackets.
	private static string Meter(int width, (int Percent, string Color)[] parts, string label)
	{
		int room = Math.Max(0, width - label.Length - 1);
		StringBuilder meter = new();
		meter.Append(Ansi.Bold).Append('[').Append(Ansi.Reset);
		int used = 0;
		foreach ((int percent, string color) in parts)
		{
			int cells = Math.Min(room - used, (int)Math.Round(percent / 100.0 * width));
			meter.Append(color).Append('|', cells).Append(Ansi.Reset);
			used += cells;
		}

		meter.Append(' ', Math.Max(0, width - used - label.Length)).Append(Ansi.Dim).Append(label).Append(Ansi.Reset);
		return meter.Append(Ansi.Bold).Append(']').Append(Ansi.Reset).ToString();
	}

	/// <summary>Every process column but the command, padded. Highlighting colors running states and busy CPUs like htop.</summary>
	private static string FormatProcess(string[] cells, bool highlight)
	{
		StringBuilder line = new();
		for (int i = 0; i < ProcessColumnWidths.Length; i++)
		{
			int width = ProcessColumnWidths[i];
			string cell = width < 0 ? cells[i].PadRight(-width) : cells[i].PadLeft(width);
			string? color = null;
			if (highlight && i == StateColumn && cells[i] == "R")
			{
				color = Ansi.BoldGreen;
			}
			else if (highlight && i == CpuColumn && double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu) && cpu >= 20)
			{
				color = Ansi.BoldRed;
			}

			line.Append(color is null ? cell : color + cell + Ansi.Reset).Append(' ');
		}

		return line.ToString();
	}

	private static string Fit(string text, int width) => text.Length > width ? text[..width] : text.PadRight(width);

	private async Task LoremAsync(string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		int paragraphs = 3;
		if (args.Length > 0 && !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out paragraphs))
		{
			output.WriteLine("usage: lorem [PARAGRAPHS]");
			return;
		}

		int width = Math.Max(Size.Columns, 20);
		StringBuilder line = new();
		for (int paragraph = 0; paragraph < paragraphs; paragraph++)
		{
			if (paragraph > 0)
			{
				output.WriteLine();
			}

			int sentences = 3 + DemoRandom.Next((ulong)paragraph, 5);
			for (int sentence = 0; sentence < sentences; sentence++)
			{
				string text = LoremSentences[DemoRandom.Next(((ulong)paragraph << 8) + (ulong)sentence, LoremSentences.Length)];
				foreach (string word in text.Split(' '))
				{
					if (line.Length > 0 && line.Length + 1 + word.Length > width)
					{
						output.WriteLine(line.ToString());
						line.Clear();
					}

					line.Append(line.Length > 0 ? " " : "").Append(word);
				}
			}

			output.WriteLine(line.ToString());
			line.Clear();
			await output.FlushIfFullAsync(cancellationToken);
		}
	}

	private void Stty(string[] args, ShellOutput output)
	{
		if (args is not ["size"])
		{
			output.WriteLine("stty: only 'stty size' works in the demo shell");
			return;
		}

		TerminalSize size = Size;
		output.WriteLine(Invariant($"{size.Rows} {size.Columns}"));
	}
}
