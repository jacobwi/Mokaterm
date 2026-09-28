using System.Globalization;
using System.Net;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.DevHost.Demo.FileSystem;
using static System.FormattableString;

namespace Mokaterm.DevHost.Demo.Terminal;

/// <summary>
/// A small fake bash with enough commands to judge terminal rendering, themes, fonts, scrolling and flow control. It
/// works on the same <see cref="DemoFileTree"/> as the session's file browser. One command runs at a time; only
/// <see cref="Size"/> is set from other threads.
/// </summary>
internal sealed partial class DemoShell
{
	private const int MaxHistory = 500;

	private readonly DemoFileTree _tree;
	private readonly DemoAccount _login;
	private readonly string _hostName;
	private readonly string _address;
	private readonly TimeProvider _timeProvider;
	private readonly List<string> _history = [];
	private readonly Lock _sizeLock = new();
	private TerminalSize _size;
	private DemoAccount _account;
	private string _directory;

	// Where the login shell was while a root shell from sudo -s runs; null otherwise.
	private string? _loginDirectory;

	public DemoShell(DemoFileTree tree, DemoAccount login, string hostName, string address, TerminalSize size, TimeProvider timeProvider)
	{
		_tree = tree;
		_login = login;
		_account = login;
		_hostName = hostName;
		_address = address;
		_timeProvider = timeProvider;
		_size = size.IsValid ? size : TerminalSize.Default;
		_directory = login.Home;
	}

	public IReadOnlyList<string> History => _history;

	/// <summary>The size the terminal view last reported.</summary>
	public TerminalSize Size
	{
		get
		{
			lock (_sizeLock)
			{
				return _size;
			}
		}

		set
		{
			lock (_sizeLock)
			{
				_size = value;
			}
		}
	}

	/// <summary>The prompt, led by the window title update that bash's default prompt sends.</summary>
	public string Prompt => Ansi.Title($"{_account.Name}@{_hostName}: {DisplayDirectory}") + VisiblePrompt;

	public string VisiblePrompt =>
		$"{Ansi.BoldGreen}{_account.Name}@{_hostName}{Ansi.Reset}:{Ansi.BoldBlue}{DisplayDirectory}{Ansi.Reset}{(_account.IsRoot ? "# " : "$ ")}";

	private string DisplayDirectory
	{
		get
		{
			string home = _account.Home;
			if (_directory == home)
			{
				return "~";
			}

			return _directory.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + _directory[home.Length..] : _directory;
		}
	}

	private DateTimeOffset Now => _timeProvider.GetUtcNow();

	/// <summary>The login banner, like Ubuntu's message of the day.</summary>
	public Task<bool> GreetAsync(ShellOutput output, CancellationToken cancellationToken)
	{
		DateTimeOffset now = Now;
		ulong seed = DemoRandom.Seed(_hostName);
		int load = DemoRandom.Next(seed, 160);
		int disk = 18 + DemoRandom.Next(seed + 1, 76);
		int memory = 12 + DemoRandom.Next(seed + 2, 80);
		int processes = 110 + DemoRandom.Next(seed + 3, 90);
		string address = IPAddress.TryParse(_address, out _)
			? _address
			: Invariant($"10.20.{DemoRandom.Next(seed + 4, 250)}.{DemoRandom.Next(seed + 5, 250) + 2}");

		output.WriteLine($"{Ansi.Bold}Welcome to Ubuntu 24.04.1 LTS{Ansi.Reset} (GNU/Linux 6.8.0-45-generic x86_64)");
		output.WriteLine();
		output.WriteLine($"  System information as of {DemoDates.Login(now)}");
		output.WriteLine();
		output.WriteLine(Stat("System load:  ", Invariant($"{load / 100.0:0.00}"), load / 4, Invariant($"Processes:             {processes}")));
		output.WriteLine(Stat("Usage of /:   ", Invariant($"{disk}% of 97.87GB"), disk, "Users logged in:       1"));
		output.WriteLine(Stat("Memory usage: ", Invariant($"{memory}%"), memory, "IPv4 address for eth0: " + address));
		output.WriteLine();
		output.WriteLine($"Last login: {DemoDates.Login(now.AddHours(-19).AddMinutes(-17))} from 192.168.1.44");
		output.WriteLine($"{Ansi.Dim}Mokaterm demo shell: nothing here touches a real machine. Type help for the commands.{Ansi.Reset}");
		return Task.FromResult(true);
	}

	/// <summary>Runs one command line. False when the login shell exited, which ends the session.</summary>
	public async Task<bool> ExecuteAsync(string line, ShellOutput output, CancellationToken cancellationToken)
	{
		string trimmed = line.Trim();
		if (trimmed.Length == 0)
		{
			return true;
		}

		Remember(trimmed);
		if (!ShellWords.TryParse(trimmed, out List<string> words))
		{
			output.WriteLine("bash: unexpected EOF while looking for matching quote");
			return true;
		}

		return words.Count == 0 || await RunAsync(words[0], [.. words.Skip(1)], output, cancellationToken);
	}

	/// <summary>Ctrl+D on an empty line: leaves the root shell, or logs out.</summary>
	public Task<bool> ExitAsync(ShellOutput output, CancellationToken cancellationToken) => Task.FromResult(Exit(output));

	private static string Stat(string label, string value, int percent, string right)
	{
		string color = percent switch
		{
			< 60 => Ansi.Green,
			< 85 => Ansi.Yellow,
			_ => Ansi.Red,
		};

		return "  " + label + color + value.PadRight(19) + Ansi.Reset + right;
	}

	private async Task<bool> RunAsync(string command, string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		switch (command)
		{
			case "help":
				Help(output);
				break;
			case "ls":
				ListFiles(args, output);
				break;
			case "cd":
				ChangeDirectory(args, output);
				break;
			case "pwd":
				output.WriteLine(_directory);
				break;
			case "cat":
				Concatenate(args, output);
				break;
			case "echo":
				Echo(args, output);
				break;
			case "whoami":
				output.WriteLine(_account.Name);
				break;
			case "clear":
				output.Write(Ansi.ClearAll);
				break;
			case "history":
				ShowHistory(output);
				break;
			case "mkdir":
				MakeDirectories(args, output);
				break;
			case "touch":
				Touch(args, output);
				break;
			case "rm":
				Remove(args, output);
				break;
			case "seq":
				await SequenceAsync(args, output, cancellationToken);
				break;
			case "colors":
				Colors(output);
				break;
			case "unicode":
				Unicode(output);
				break;
			case "top":
				Top(output);
				break;
			case "tail":
				await TailAsync(args, output, cancellationToken);
				break;
			case "lorem":
				await LoremAsync(args, output, cancellationToken);
				break;
			case "stty":
				Stty(args, output);
				break;
			case "sudo":
				return await SudoAsync(args, output, cancellationToken);
			case "exit" or "logout":
				return Exit(output);
			default:
				output.WriteLine($"bash: {command}: command not found");
				break;
		}

		return true;
	}

	private async Task<bool> SudoAsync(string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		switch (args)
		{
			case []:
				output.WriteLine("usage: sudo -s | sudo -i | sudo COMMAND");
				return true;

			case ["-s" or "-i" or "su" or "bash"]:
				if (!_account.IsRoot)
				{
					_loginDirectory = _directory;
					_account = DemoAccount.Root;
					_directory = args[0] == "-i" ? DemoAccount.Root.Home : _directory;
				}

				return true;

			case ["cd" or "exit" or "logout" or "history" or "sudo", ..]:
				// Shell builtins are not programs sudo could run.
				output.WriteLine($"sudo: {args[0]}: command not found");
				return true;
		}

		DemoAccount caller = _account;
		_account = DemoAccount.Root;
		try
		{
			return await RunAsync(args[0], args[1..], output, cancellationToken);
		}
		finally
		{
			_account = caller;
		}
	}

	private bool Exit(ShellOutput output)
	{
		if (_loginDirectory is { } loginDirectory)
		{
			output.WriteLine("exit");
			_account = _login;
			_directory = loginDirectory;
			_loginDirectory = null;
			return true;
		}

		output.WriteLine("logout");
		return false;
	}

	private void ChangeDirectory(string[] args, ShellOutput output)
	{
		if (args.Length > 1)
		{
			output.WriteLine("bash: cd: too many arguments");
			return;
		}

		string path = args.Length == 0 ? _account.Home : PathOf(args[0]);
		try
		{
			_tree.RequireEnterable(path, _account);

			// Kept as typed, links and all, like bash's logical working directory.
			_directory = path;
		}
		catch (RemoteFileSystemException ex)
		{
			output.WriteLine($"bash: cd: {(args.Length == 0 ? path : args[0])}: {ex.Message}");
		}
	}

	private void ShowHistory(ShellOutput output)
	{
		for (int i = 0; i < _history.Count; i++)
		{
			output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{i + 1,5}  {_history[i]}"));
		}
	}

	private void Remember(string line)
	{
		if (_history.Count > 0 && _history[^1] == line)
		{
			return;
		}

		if (_history.Count == MaxHistory)
		{
			_history.RemoveAt(0);
		}

		_history.Add(line);
	}

	/// <summary>An absolute path for a command argument: <c>~</c> is the home folder and relative paths start in the working folder.</summary>
	private string PathOf(string argument)
	{
		if (argument == "~")
		{
			return _account.Home;
		}

		return argument.StartsWith("~/", StringComparison.Ordinal)
			? RemotePath.Combine(_account.Home, argument[2..])
			: RemotePath.Combine(_directory, argument);
	}
}
