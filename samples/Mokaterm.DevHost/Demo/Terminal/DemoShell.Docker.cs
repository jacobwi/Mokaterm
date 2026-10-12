using System.Globalization;

namespace Mokaterm.DevHost.Demo.Terminal;

// Just enough of exec and docker exec for the Docker manager's shell tab, which types "exec docker exec -it NAME bash"
// into a new session. The demo has no container to run a shell in, so the prompt becomes the container's and the
// commands keep working on the demo host's files.
internal sealed partial class DemoShell
{
	// The container a "docker exec -it" shell is in, and the host folder to go back to; null outside one.
	private string? _container;
	private string _hostDirectory = "/";

	// Set when exec put the container's shell in the login shell's place, so leaving it ends the session.
	private bool _containerReplacedLogin;

	private string ContainerId =>
		DemoRandom.Mix(DemoRandom.Seed(_container ?? "")).ToString("x16", CultureInfo.InvariantCulture)[..12];

	/// <summary>Runs the rest of the line in the shell's place: when it ends, so does the session.</summary>
	private async Task<bool> ExecAsync(string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		if (args.Length == 0)
		{
			return true;
		}

		bool keepRunning = await RunAsync(args[0], args[1..], output, cancellationToken);
		if (_container is not null && keepRunning)
		{
			_containerReplacedLogin = true;
			return true;
		}

		return false;
	}

	private void Docker(string[] args, ShellOutput output)
	{
		if (args is not ["exec", .. string[] rest])
		{
			output.WriteLine("demo: this shell only knows docker exec -it NAME bash. The Docker button on the toolbar has the rest.");
			return;
		}

		int index = 0;
		bool interactive = false;
		bool terminal = false;
		while (index < rest.Length && rest[index].StartsWith('-'))
		{
			interactive |= rest[index].Contains('i', StringComparison.Ordinal);
			terminal |= rest[index].Contains('t', StringComparison.Ordinal);
			index++;
		}

		if (!interactive || !terminal || rest.Length - index != 2 || rest[index + 1] is not ("bash" or "sh"))
		{
			output.WriteLine("demo: only docker exec -it NAME bash (or sh) opens a shell here.");
			return;
		}

		if (_container is not null)
		{
			output.WriteLine("demo: this shell is already inside a container.");
			return;
		}

		_container = rest[index];
		_hostDirectory = _directory;
		_directory = "/";
		output.WriteLine($"{Ansi.Dim}Demo: a real session would now be in {rest[index + 1]} inside {_container}. This one keeps the demo host's files.{Ansi.Reset}");
	}

	/// <summary>Leaves the container's shell. False when that shell had replaced the login shell, which ends the session.</summary>
	private bool LeaveContainer(ShellOutput output)
	{
		output.WriteLine("exit");
		if (_containerReplacedLogin)
		{
			return false;
		}

		_container = null;
		_directory = _hostDirectory;
		return true;
	}
}
