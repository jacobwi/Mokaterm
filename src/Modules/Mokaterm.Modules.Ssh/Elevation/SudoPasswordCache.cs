using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>A sudo password the user chose to remember for the rest of the session. Wiped when the session closes.</summary>
internal sealed class SudoPasswordCache : IDisposable
{
	private readonly Lock _lock = new();
	private SecretBuffer? _password;

	/// <summary>An independent copy the caller owns, or null.</summary>
	public SecretBuffer? CopyPassword()
	{
		lock (_lock)
		{
			return _password?.Copy();
		}
	}

	/// <summary>Takes ownership of <paramref name="password"/>.</summary>
	public void Remember(SecretBuffer password)
	{
		SecretBuffer? previous;
		lock (_lock)
		{
			previous = _password;
			_password = password;
		}

		previous?.Dispose();
	}

	public void Forget()
	{
		SecretBuffer? previous;
		lock (_lock)
		{
			previous = _password;
			_password = null;
		}

		previous?.Dispose();
	}

	public void Dispose() => Forget();
}
