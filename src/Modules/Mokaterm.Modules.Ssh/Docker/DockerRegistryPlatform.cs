namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>One platform's manifest in a registry: its digest and, where the tool printed it, its image config's digest.</summary>
internal sealed record DockerRegistryPlatform(string Os, string Architecture, string Variant, string Digest, string? ConfigDigest = null)
{
	/// <summary><c>linux/arm/v7</c> as its three parts.</summary>
	public static DockerRegistryPlatform FromText(string platform, string digest, string? configDigest = null)
	{
		ArgumentNullException.ThrowIfNull(platform);
		string[] parts = platform.Trim().Split('/');
		return new DockerRegistryPlatform(
			parts.Length > 0 ? parts[0] : "",
			parts.Length > 1 ? parts[1] : "",
			parts.Length > 2 ? parts[2] : "",
			digest,
			configDigest);
	}

	public string Display => Variant.Length > 0 ? $"{Os}/{Architecture}/{Variant}" : $"{Os}/{Architecture}";
}
