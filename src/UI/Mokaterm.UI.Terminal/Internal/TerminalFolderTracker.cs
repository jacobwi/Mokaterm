using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.Terminal.Internal;

/// <summary>
/// Turns what terminal.js reports while a view follows its shell's folder into the changes worth acting on: an OSC 7 URI
/// or a prompt's folder, each passed on only when it differs from the last one passed on. Once an OSC 7 arrived prompts
/// are ignored, because the shell then says where it is itself. Reports naming another machine than the first one named
/// are dropped: that is an ssh run inside the session, whose folders are not on this server.
/// </summary>
internal sealed class TerminalFolderTracker
{
	/// <summary>The longest report taken; the page cuts an OSC 7 to the same length.</summary>
	public const int MaxReportLength = 4096;

	private string? _machine;
	private bool _sawOsc7;
	private TerminalFolder? _last;

	/// <summary>Forgets everything, for a view that starts following again.</summary>
	public void Reset()
	{
		_machine = null;
		_sawOsc7 = false;
		_last = null;
	}

	/// <summary>The folder an OSC 7 sequence names, when it is new; null otherwise.</summary>
	public TerminalFolder? FromOsc7(string data)
	{
		if (!TryParseOsc7(data, out string? machine, out string path))
		{
			return null;
		}

		_sawOsc7 = true;
		return Accept(machine, new TerminalFolder(path, User: null));
	}

	/// <summary>The folder a <c>user@machine:folder</c> prompt shows, when it is new and no OSC 7 has arrived; null otherwise.</summary>
	public TerminalFolder? FromPrompt(string folder, string? user, string? machine)
	{
		if (_sawOsc7 || string.IsNullOrEmpty(folder) || folder.Length > MaxReportLength || folder.Any(char.IsControl))
		{
			return null;
		}

		return Accept(string.IsNullOrEmpty(machine) ? null : machine, new TerminalFolder(folder, string.IsNullOrEmpty(user) ? null : user));
	}

	/// <summary>
	/// Reads <c>file://host/path</c>, percent-encoded as a URI is, or kitty's <c>kitty-shell-cwd://host/path</c>, which is
	/// not. An empty host or <c>localhost</c> is the machine the shell runs on, so it names none. The path comes back
	/// absolute and normalized; one holding a control character is refused.
	/// </summary>
	public static bool TryParseOsc7(string data, out string? machine, out string path)
	{
		machine = null;
		path = "";
		if (string.IsNullOrEmpty(data) || data.Length > MaxReportLength)
		{
			return false;
		}

		string rest;
		bool encoded;
		if (data.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
		{
			rest = data["file://".Length..];
			encoded = true;
		}
		else if (data.StartsWith("kitty-shell-cwd://", StringComparison.OrdinalIgnoreCase))
		{
			rest = data["kitty-shell-cwd://".Length..];
			encoded = false;
		}
		else
		{
			return false;
		}

		int slash = rest.IndexOf('/', StringComparison.Ordinal);
		if (slash < 0)
		{
			return false;
		}

		string host = rest[..slash];
		string decoded = encoded ? Uri.UnescapeDataString(rest[slash..]) : rest[slash..];
		if (decoded.Any(char.IsControl))
		{
			return false;
		}

		machine = host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? null : host;
		path = RemotePath.Normalize(decoded);
		return true;
	}

	/// <summary>A prompt shows the short name (<c>\h</c>) and OSC 7 often the full one, so only the first label is compared.</summary>
	internal static bool IsSameMachine(string first, string second) =>
		string.Equals(FirstLabel(first), FirstLabel(second), StringComparison.OrdinalIgnoreCase);

	private static string FirstLabel(string machine)
	{
		int dot = machine.IndexOf('.', StringComparison.Ordinal);
		return dot > 0 ? machine[..dot] : machine;
	}

	private TerminalFolder? Accept(string? machine, TerminalFolder folder)
	{
		if (machine is not null)
		{
			_machine ??= machine;
			if (!IsSameMachine(_machine, machine))
			{
				return null;
			}
		}

		if (folder == _last)
		{
			return null;
		}

		_last = folder;
		return folder;
	}
}
