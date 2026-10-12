namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>One user's <c>~/.ssh/authorized_keys</c> as the users page shows it.</summary>
internal sealed record AuthorizedKeysFile
{
	public required AuthorizedKeysState State { get; init; }

	/// <summary>The home folder passwd names, or empty when it names none.</summary>
	public string Home { get; init; } = "";

	public IReadOnlyList<AuthorizedKey> Keys { get; init; } = [];

	/// <summary><c>~/.ssh</c> or the file is a symbolic link, which nothing here writes through.</summary>
	public bool IsLink { get; init; }

	/// <summary>
	/// Why sshd may ignore the file although it is there: a folder or file others can write to, which OpenSSH's
	/// <c>StrictModes</c> refuses. Null when the modes are fine or were not read.
	/// </summary>
	public string? ModeProblem { get; init; }

	/// <summary>The file was longer than the read keeps, so keys past that are not listed.</summary>
	public bool IsCut { get; init; }

	/// <summary>The path of the file, or empty when there is no home.</summary>
	public string Path => Home.Length == 0 ? "" : Home.TrimEnd('/') + "/.ssh/authorized_keys";

	/// <summary>The key with this fingerprint, or null when the file does not hold it.</summary>
	public AuthorizedKey? Find(string? fingerprint) =>
		string.IsNullOrEmpty(fingerprint) ? null : Keys.FirstOrDefault(key => string.Equals(key.Fingerprint, fingerprint, StringComparison.Ordinal));
}
