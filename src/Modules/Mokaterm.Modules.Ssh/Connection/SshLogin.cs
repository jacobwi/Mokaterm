using System.Security.Cryptography;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Renci.SshNet;
using Renci.SshNet.Common;
using AuthenticationMethod = Renci.SshNet.AuthenticationMethod;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// The SSH.NET authentication methods for one connection attempt. Keyboard-interactive always comes last so servers that
/// ask for a second factor after a password or key still work: a single password-looking question is answered once with
/// the saved password, everything else goes to the user.
/// </summary>
internal sealed class SshLogin : IDisposable
{
	private readonly LoginCredentials _credentials;
	private readonly IUserInteraction _interaction;
	private readonly string _account;
	private readonly TimeSpan _promptTimeout;
	private readonly ConnectWatchdog _watchdog;
	private readonly CancellationToken _cancellationToken;
	private readonly byte[]? _passwordBytes;
	private readonly List<AuthenticationMethod> _methods = [];
	private int _passwordAnswered;

	/// <param name="keySource">The loaded key for <see cref="LoginMethod.PublicKey"/> or the agent's keys for <see cref="LoginMethod.Agent"/>; not owned.</param>
	/// <exception cref="ProtocolConnectException">The credentials cannot log in over SSH.</exception>
	public SshLogin(LoginCredentials credentials, IPrivateKeySource? keySource, IUserInteraction interaction, string account, TimeSpan promptTimeout, ConnectWatchdog watchdog, CancellationToken cancellationToken)
	{
		_credentials = credentials;
		_interaction = interaction;
		_account = account;
		_promptTimeout = promptTimeout;
		_watchdog = watchdog;
		_cancellationToken = cancellationToken;

		switch (credentials.Method)
		{
			case LoginMethod.Password:
				if (credentials.Password is not { } password)
				{
					throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "No password was provided.");
				}

				// SSH.NET takes the password as bytes, so it never has to become a string; wiped on dispose.
				_passwordBytes = GC.AllocateArray<byte>(password.Length, pinned: true);
				password.Span.CopyTo(_passwordBytes);
				_methods.Add(new PasswordAuthenticationMethod(credentials.Username, _passwordBytes));
				break;
			case LoginMethod.PublicKey:
				_methods.Add(keySource is not null
					? new PrivateKeyAuthenticationMethod(credentials.Username, keySource)
					: throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "No private key was provided."));
				break;
			case LoginMethod.Agent:
				// The agent signs; SSH.NET only ever sees a key algorithm that delegates to it.
				_methods.Add(keySource is not null
					? new PrivateKeyAuthenticationMethod(credentials.Username, keySource)
					: throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "No SSH agent key was available."));
				break;
			case LoginMethod.KeyboardInteractive:
				break;
			default:
				throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, $"{credentials.Method} logins are not supported over SSH.");
		}

		KeyboardInteractiveAuthenticationMethod keyboardInteractive = new(credentials.Username);
		keyboardInteractive.AuthenticationPrompt += OnAuthenticationPrompt;
		_methods.Add(keyboardInteractive);
	}

	public AuthenticationMethod[] Methods => [.. _methods];

	/// <summary>The user dismissed a server prompt, so the attempt ends as cancelled rather than as a failed login.</summary>
	public bool PromptCancelled { get; private set; }

	/// <summary>A server prompt stayed unanswered for the prompt timeout.</summary>
	public bool PromptTimedOut { get; private set; }

	public void Dispose()
	{
		foreach (AuthenticationMethod method in _methods)
		{
			if (method is KeyboardInteractiveAuthenticationMethod keyboardInteractive)
			{
				keyboardInteractive.AuthenticationPrompt -= OnAuthenticationPrompt;
			}

			method.Dispose();
		}

		if (_passwordBytes is not null)
		{
			CryptographicOperations.ZeroMemory(_passwordBytes);
		}
	}

	/// <summary>True for prompts such as <c>Password:</c> or <c>abc@host's password:</c>, not for new-password or code prompts.</summary>
	internal static bool IsPasswordPrompt(string request) =>
		request.Contains("password", StringComparison.OrdinalIgnoreCase)
		&& !request.Contains("new password", StringComparison.OrdinalIgnoreCase)
		&& !request.Contains("retype", StringComparison.OrdinalIgnoreCase);

	private void OnAuthenticationPrompt(object? sender, AuthenticationPromptEventArgs e)
	{
		if (e.Prompts.Count == 0)
		{
			return;
		}

		if (TryAnswerWithSavedPassword(e))
		{
			return;
		}

		KeyboardInteractivePrompt prompt = new()
		{
			Title = $"Log in to {_account}",
			Instruction = string.IsNullOrWhiteSpace(e.Instruction) ? null : e.Instruction,
			Questions = [.. e.Prompts.Select(question => new KeyboardInteractiveQuestion(question.Request, question.IsEchoed))],
		};

		IReadOnlyList<string>? answers;
		_watchdog.Suspend();
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
		try
		{
			timeout.CancelAfter(_promptTimeout);

			// SSH.NET raises this event on a thread of its own and reads the responses as soon as the handler returns,
			// so the prompt has to be awaited synchronously here.
			answers = _interaction.PromptKeyboardInteractiveAsync(prompt, timeout.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
			answers = null;
		}
		finally
		{
			_watchdog.Arm();
		}

		if (answers is null || answers.Count != e.Prompts.Count)
		{
			// Leaving the responses unset makes SSH.NET fail this method.
			if (timeout.IsCancellationRequested && !_cancellationToken.IsCancellationRequested)
			{
				PromptTimedOut = true;
			}
			else
			{
				PromptCancelled = true;
			}

			return;
		}

		for (int i = 0; i < answers.Count; i++)
		{
			e.Prompts[i].Response = answers[i];
		}
	}

	private bool TryAnswerWithSavedPassword(AuthenticationPromptEventArgs e)
	{
		if (_credentials.Password is null
			|| e.Prompts.Count != 1
			|| e.Prompts[0].IsEchoed
			|| !IsPasswordPrompt(e.Prompts[0].Request)
			|| Interlocked.Exchange(ref _passwordAnswered, 1) != 0)
		{
			return false;
		}

		try
		{
			// AuthenticationPrompt.Response is a string: this is the library boundary that insists on one.
			e.Prompts[0].Response = _credentials.Password.RevealString();
			return true;
		}
		catch (ObjectDisposedException)
		{
			// The attempt was abandoned and its credentials wiped.
			return false;
		}
	}
}
