namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>The one wording for where a registry login stands, for the registries page, the image panel and the push question.</summary>
internal static class DockerRegistryWording
{
	/// <summary>Whose docker it is: "docker as alice", or "root's docker" through sudo.</summary>
	public static string Whose(DockerClientConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);
		return config.User switch
		{
			"root" => "root's docker",
			{ Length: > 0 } user => $"docker as {user}",
			_ => "this docker",
		};
	}

	/// <summary>Where the credentials of one registry are, as far as the client config says.</summary>
	public static string Storage(DockerClientConfig config, DockerRegistryEntry entry)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(entry);
		if (entry.Helper is { } helper)
		{
			return $"Kept by the credential helper docker-credential-{helper}.";
		}

		if (entry.StoredInFile)
		{
			return $"Stored in {config.Path}, base64 and not encrypted.";
		}

		return config.CredentialsStore is { } store
			? $"Kept by the credential store docker-credential-{store}."
			: "Logged in, with no credential in the file.";
	}

	/// <summary>Whether a push to <paramref name="registry"/> goes out logged in, in a sentence.</summary>
	public static string LoginState(DockerClientConfig config, string registry)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(registry);
		string whose = Whose(config);
		string name = DockerRegistryNames.Display(registry);
		DockerRegistryEntry? entry = config.Find(registry);
		if (entry is { LoggedIn: true })
		{
			return $"{Capital(whose)} is logged in to {name}. {Storage(config, entry)}";
		}

		if (entry?.Helper is { } helper)
		{
			return $"{Capital(whose)} asks docker-credential-{helper} for {name}'s credentials, whatever it holds.";
		}

		return $"{Capital(whose)} is not logged in to {name}, so a registry that wants a login refuses the push. Log in on the Registries page first.";
	}

	private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
