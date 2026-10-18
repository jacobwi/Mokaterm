using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// The demo's registries: the client config the Registries page reads, <c>docker login</c> (the password comes on stdin
/// and only <see cref="RegistryPassword"/> is taken), <c>docker logout</c> and <c>docker image tag</c>. ghcr.io starts
/// logged in with its credential in the file, as docker leaves it without a helper, and an ECR registry has a helper.
/// </summary>
internal sealed partial class DemoDockerEngine
{
	/// <summary>The password every demo registry takes, which a refused login names so it can be tried.</summary>
	public const string RegistryPassword = "demo";

	private const string ConfigPath = "/home/demo/.docker/config.json";

	private const string HubKey = "https://index.docker.io/v1/";

	private const string EcrRegistry = "123456789012.dkr.ecr.eu-west-1.amazonaws.com";

	/// <summary>Registry key, as the config would have it, to the user logged in there.</summary>
	private readonly Dictionary<string, string> _logins = new(StringComparer.Ordinal) { ["ghcr.io"] = "acme-ci" };

	/// <summary>
	/// <c>login --username=USER --password-stdin [SERVER]</c> with the password on stdin. docker reads all of stdin and
	/// drops one line break at the end, and so does this.
	/// </summary>
	public DemoDockerResult Login(IReadOnlyList<string> words, string? input)
	{
		ArgumentNullException.ThrowIfNull(words);
		string? user = words.FirstOrDefault(word => word.StartsWith("--username=", StringComparison.Ordinal))?["--username=".Length..];
		if (user is null || !words.Contains("--password-stdin"))
		{
			return DemoDockerResult.Fail(1, "Error: Cannot perform an interactive login from a non TTY device");
		}

		string server = words.LastOrDefault(word => !word.StartsWith('-')) ?? HubKey;
		string password = (input ?? "").TrimEnd('\n').TrimEnd('\r');
		if (!string.Equals(password, RegistryPassword, StringComparison.Ordinal))
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: Get \"https://{HostOf(server)}/v2/\": unauthorized: incorrect username or password (the DevHost's demo registries take the password {RegistryPassword})");
		}

		lock (_lock)
		{
			_logins.Remove(_logins.Keys.FirstOrDefault(key => HostOf(key) == HostOf(server)) ?? "");
			_logins[KeyFor(server)] = user;
		}

		return new DemoDockerResult(
			0,
			"Login Succeeded\n",
			$"WARNING! Your credentials are stored unencrypted in '{ConfigPath}'.\nConfigure a credential helper to remove this warning. See\nhttps://docs.docker.com/go/credential-store/\n\n");
	}

	/// <summary>
	/// The registry key a login is kept under: Docker Hub's own for every name it goes by, otherwise the server as given.
	/// </summary>
	private static string KeyFor(string server) => HostOf(server) == "docker.io" ? HubKey : server;

	/// <summary>The host a key or a reference's registry names, lower case, Docker Hub as <c>docker.io</c>.</summary>
	private static string HostOf(string server)
	{
		string text = server;
		int scheme = text.IndexOf("://", StringComparison.Ordinal);
		if (scheme >= 0)
		{
			text = text[(scheme + 3)..];
		}

		int slash = text.IndexOf('/', StringComparison.Ordinal);
		text = (slash >= 0 ? text[..slash] : text).ToLowerInvariant();
		return text is "index.docker.io" or "registry-1.docker.io" or "" ? "docker.io" : text;
	}

	/// <summary>The registry host of an image reference, the way docker reads it.</summary>
	private static string RegistryOf(string reference)
	{
		int slash = reference.IndexOf('/', StringComparison.Ordinal);
		string first = slash > 0 ? reference[..slash] : "";
		return first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal) || first == "localhost"
			? first.ToLowerInvariant()
			: "docker.io";
	}

	private DemoDockerResult Logout(string server)
	{
		string? key = _logins.Keys.FirstOrDefault(known => HostOf(known) == HostOf(server));
		if (key is null)
		{
			return DemoDockerResult.Ok($"Not logged in to {server}\n");
		}

		_logins.Remove(key);
		return DemoDockerResult.Ok($"Removing login credentials for {HostOf(server)}\n");
	}

	/// <summary>
	/// What the client config script prints: the account, the file, the marker and the file as the server's sed leaves
	/// it, every credential value blanked to <c>"1"</c>.
	/// </summary>
	private DemoDockerResult ClientConfig()
	{
		StringBuilder json = new("{\n\t\"auths\": {");
		bool first = true;
		foreach (string key in _logins.Keys.Order(StringComparer.Ordinal))
		{
			json.Append(first ? "\n" : ",\n").Append("\t\t").Append(JsonSerializer.Serialize(key)).Append(": {\n\t\t\t\"auth\": \"1\"\n\t\t}");
			first = false;
		}

		json.Append(first ? "},\n" : "\n\t},\n")
			.Append("\t\"credHelpers\": {\n\t\t").Append(JsonSerializer.Serialize(EcrRegistry)).Append(": \"ecr-login\"\n\t}\n}\n");
		return DemoDockerResult.Ok($"demo\n{ConfigPath}\n@@mt:config\n{json}");
	}

	/// <summary><c>image tag SOURCE TARGET</c>: the target names the source's image from now on, moved off any other.</summary>
	private DemoDockerResult TagImage(string source, string target)
	{
		int index = _images.FindIndex(image => image.Id == source || image.Id.EndsWith(source, StringComparison.Ordinal) || $"{image.Repository}:{image.Tag}" == Tagged(source));
		if (index < 0)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such image: {source}");
		}

		string tagged = Tagged(target);
		string repository = RepositoryOf(tagged);
		string tag = tagged[(tagged.LastIndexOf(':') + 1)..];
		(_, _, string id, long size, DateTimeOffset created) = _images[index];
		_images.RemoveAll(image => image.Repository == repository && image.Tag == tag);
		_images.Add((repository, tag, id, size, created));
		_pulled.Remove(tagged);
		return DemoDockerResult.Ok();
	}

	/// <summary>A push's layer lines, the same few for every image, so a push reads like one.</summary>
	private static IEnumerable<DemoDockerLine> LayerLines(string tagged, bool known)
	{
		string[] layers = ["5f70bf18a086", "a1c2d3e4f501", "9b8c7d6e5f40"];
		foreach (string layer in layers)
		{
			yield return new DemoDockerLine(TimeSpan.FromMilliseconds(150), $"{layer}: Preparing", false);
		}

		foreach (string layer in layers)
		{
			yield return new DemoDockerLine(TimeSpan.FromMilliseconds(known ? 200 : 900), known ? $"{layer}: Layer already exists" : $"{layer}: Pushed", false);
		}

		string size = (1570 + (tagged.Length * 7)).ToString(CultureInfo.InvariantCulture);
		yield return new DemoDockerLine(TimeSpan.FromMilliseconds(300), $"{tagged[(tagged.LastIndexOf(':') + 1)..]}: digest: {Digest("pushed " + tagged)} size: {size}", false);
	}
}
