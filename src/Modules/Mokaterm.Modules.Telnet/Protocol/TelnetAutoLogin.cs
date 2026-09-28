using System.Text;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// Types a saved user name and password when the stream shows a login prompt. Telnet has no authentication of its
/// own: this only automates what the user would type, and it travels in clear text like everything else.
/// </summary>
internal sealed class TelnetAutoLogin : IDisposable
{
	/// <summary>How much of the current line is kept. A prompt is far shorter; a runaway line must not grow memory.</summary>
	private const int MaxLineLength = 512;

	private static readonly string[] UsernamePrompts =
	[
		"login:",
		"login as:",
		"username:",
		"user name:",
		"user:",
		"user id:",
	];

	private static readonly string[] PasswordPrompts =
	[
		"password:",
		"passcode:",
		"passphrase:",
	];

	private readonly StringBuilder _line = new();
	private readonly byte[]? _username;
	private SecretBuffer? _password;
	private Stage _stage;
	private EscapeState _escape;
	private bool _typedUsername;

	/// <param name="username">The name to type at a login prompt, or null to wait for the password prompt only.</param>
	/// <param name="password">Owned by this instance and wiped when it is disposed.</param>
	public TelnetAutoLogin(string? username, SecretBuffer? password)
	{
		_username = string.IsNullOrEmpty(username) ? null : Encoding.UTF8.GetBytes(username);
		_password = password;
		_stage = _username is not null ? Stage.Username
			: password is not null ? Stage.Password
			: Stage.Done;
	}

	private enum Stage
	{
		Username,
		Password,
		Done,
	}

	private enum EscapeState
	{
		None,
		Escape,
		ControlSequence,
		StringSequence,
		StringEscape,
	}

	/// <summary>True once the login has been typed, or when there was nothing to type.</summary>
	public bool IsFinished => _stage == Stage.Done;

	/// <summary>True when this login has a password to type.</summary>
	public bool HasPassword => _password is not null;

	/// <summary>
	/// Reads more output from the server and returns the answer to type, or null while no prompt has shown up.
	/// </summary>
	public TelnetAutoLoginStep? Feed(ReadOnlySpan<char> text)
	{
		if (_stage == Stage.Done)
		{
			return null;
		}

		Append(text);
		string line = _line.ToString().TrimEnd();
		if (_stage == Stage.Username)
		{
			if (!EndsWithPrompt(line, UsernamePrompts))
			{
				return null;
			}

			_line.Clear();
			_typedUsername = true;
			_stage = _password is not null ? Stage.Password : Stage.Done;
			return new TelnetAutoLoginStep(_username!, Echo: true);
		}

		if (_typedUsername && EndsWithPrompt(line, UsernamePrompts))
		{
			// The login prompt came back before the password prompt: the name was refused or the device started
			// over. The password belonged to the attempt this login typed, so the user takes it from here.
			Dispose();
			return null;
		}

		if (!EndsWithPrompt(line, PasswordPrompts))
		{
			return null;
		}

		byte[] password = _password!.Span.ToArray();
		_password.Dispose();
		_password = null;
		_line.Clear();
		_stage = Stage.Done;
		return new TelnetAutoLoginStep(password, Echo: false);
	}

	/// <summary>
	/// The user pressed Enter. Once this login has typed the user name, that means the user took over: a password
	/// prompt after it belongs to whatever they typed (su, enable, another login), so the login stops. Before the
	/// name, Enter is how a quiet device or a console server is asked for its prompt, which changes nothing.
	/// </summary>
	public void LineSubmitted()
	{
		if (_typedUsername && _stage == Stage.Password)
		{
			Dispose();
		}
	}

	public void Dispose()
	{
		_password?.Dispose();
		_password = null;
		_line.Clear();
		_stage = Stage.Done;
	}

	private static bool EndsWithPrompt(string line, string[] prompts) =>
		Array.Exists(prompts, prompt => line.EndsWith(prompt, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Keeps the line being written, with escape sequences and other control characters left out, so a prompt drawn
	/// in colour still matches.
	/// </summary>
	private void Append(ReadOnlySpan<char> text)
	{
		foreach (char value in text)
		{
			switch (_escape)
			{
				case EscapeState.Escape:
					_escape = value switch
					{
						'[' => EscapeState.ControlSequence,
						']' or 'P' or '^' or '_' => EscapeState.StringSequence,
						_ => EscapeState.None,
					};

					continue;

				case EscapeState.ControlSequence:
					if (value is >= '@' and <= '~')
					{
						_escape = EscapeState.None;
					}

					continue;

				case EscapeState.StringSequence:
					_escape = value switch
					{
						'' => EscapeState.None,
						'' => EscapeState.StringEscape,
						_ => EscapeState.StringSequence,
					};

					continue;

				case EscapeState.StringEscape:
					_escape = value == '\\' ? EscapeState.None : EscapeState.StringSequence;
					continue;

				case EscapeState.None:
				default:
					break;
			}

			if (value == '')
			{
				_escape = EscapeState.Escape;
			}
			else if (value is '\r' or '\n')
			{
				_line.Clear();
			}
			else if (!char.IsControl(value) && _line.Length < MaxLineLength)
			{
				_line.Append(value);
			}
		}
	}
}
