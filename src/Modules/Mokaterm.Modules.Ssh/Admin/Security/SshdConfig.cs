namespace Mokaterm.Modules.Ssh.Admin.Security;

/// <summary>
/// The SSH server's settings as the security page reads them: the effective values (from <c>sshd -T</c> as root, else
/// worked out from the files the same way sshd reads them), the files they came from, and where a change would have to
/// go to win.
/// </summary>
internal sealed record SshdConfig
{
	/// <summary>The main configuration file.</summary>
	public const string MainFile = "/etc/ssh/sshd_config";

	/// <summary>The folder distributions include from the top of <see cref="MainFile"/>.</summary>
	public const string DropInFolder = "/etc/ssh/sshd_config.d";

	/// <summary>
	/// The drop-in the security page writes. sshd takes the first value it reads for a keyword and reads drop-ins in
	/// name order, so the name sorts ahead of the usual <c>50-cloud-init.conf</c>.
	/// </summary>
	public const string DropInFile = DropInFolder + "/00-mokaterm.conf";

	/// <summary>Nothing was read: no OpenSSH server, or the read did not get that far.</summary>
	public static SshdConfig None { get; } = new();

	/// <summary>An OpenSSH sshd is installed.</summary>
	public bool Installed { get; init; }

	/// <summary>The values are sshd's own (<c>sshd -T</c>), not worked out from the files.</summary>
	public bool Effective { get; init; }

	/// <summary>What <c>sshd -T</c> said when it refused to print the settings: usually a broken configuration.</summary>
	public string? TestProblem { get; init; }

	/// <summary>The value of each keyword, keywords in lower case, values as written (sshd -T writes them lower case too).</summary>
	public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Which file set each keyword, when the files were read; a keyword left at sshd's default has none.</summary>
	public IReadOnlyDictionary<string, string> Sources { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Every file read, in the order sshd reads them.</summary>
	public IReadOnlyList<string> Files { get; init; } = [];

	/// <summary>Files that exist but could not be read; on RHEL the main file is root's alone.</summary>
	public IReadOnlyList<string> Unreadable { get; init; } = [];

	/// <summary>The main file includes <see cref="DropInFolder"/> before any Match block.</summary>
	public bool IncludesDropIns { get; init; }

	/// <summary>Keywords the main file sets before its include, which a drop-in can therefore never change.</summary>
	public IReadOnlySet<string> SetBeforeInclude { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>The main file was read, so where a change goes can be told.</summary>
	public bool KnowsFiles => Files.Contains(MainFile);

	/// <summary>The value of <paramref name="keyword"/> (any case), or null when neither sshd nor the files say.</summary>
	public string? Get(string keyword)
	{
		ArgumentNullException.ThrowIfNull(keyword);
		return Values.TryGetValue(keyword.ToLowerInvariant(), out string? value) ? value : null;
	}

	/// <summary>
	/// Where a new value for <paramref name="keyword"/> has to go to win: <see cref="DropInFile"/> when the main file
	/// includes the drop-ins before setting it and no drop-in that sorts first sets it, else the file that sets it now
	/// (or the main file), which the user edits. Null when the files were not read.
	/// </summary>
	public SshdChangeRoute? RouteFor(string keyword)
	{
		ArgumentNullException.ThrowIfNull(keyword);
		if (!KnowsFiles)
		{
			return null;
		}

		string key = keyword.ToLowerInvariant();
		string? source = Sources.TryGetValue(key, out string? file) ? file : null;
		if (!IncludesDropIns || SetBeforeInclude.Contains(key))
		{
			return new SshdChangeRoute(false, source ?? MainFile);
		}

		bool earlierDropIn = source is not null
			&& source.StartsWith(DropInFolder + "/", StringComparison.Ordinal)
			&& string.CompareOrdinal(source, DropInFile) < 0;
		return earlierDropIn ? new SshdChangeRoute(false, source!) : new SshdChangeRoute(true, DropInFile);
	}
}
