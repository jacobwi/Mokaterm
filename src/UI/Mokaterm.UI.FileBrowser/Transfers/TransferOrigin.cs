namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>Where browser transfers come from: the queue group (the session title) and the host shown in their labels.</summary>
internal sealed record TransferOrigin(string Group, string Host)
{
	/// <summary><c>abc@host:/etc/hosts</c>, or <c>root@host:...</c> for a transfer that runs as root.</summary>
	public string Describe(TransferFileSystem fileSystem, string path) =>
		Format(fileSystem.IsElevated ? "root" : fileSystem.UserName, Host, path);

	/// <summary><c>user@host:/path</c>, with an IPv6 host in brackets so it does not run into the path.</summary>
	public static string Format(string user, string host, string path) =>
		user + "@" + (host.Contains(':', StringComparison.Ordinal) ? "[" + host + "]" : host) + ":" + path;
}
