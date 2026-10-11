namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// A system view command that did not run to its end, with a message written for the user: the connection went away,
/// sudo refused, the time ran out, or the server never started the script. A command that ran and failed is not this;
/// its exit code and output come back in an <see cref="AdminOutcome"/>.
/// </summary>
internal sealed class AdminException : Exception
{
	public AdminException()
	{
	}

	public AdminException(string message)
		: base(message)
	{
	}

	public AdminException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	/// <summary>sudo turned the login down, so the root mode that was on has to be asked for again.</summary>
	public bool RootRefused { get; init; }
}
