using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>The parts of <c>docker info --format '{{json .}}'</c> the overview shows.</summary>
internal sealed record DockerEngineInfo
{
	public int Containers { get; init; }

	public int Running { get; init; }

	public int Paused { get; init; }

	public int Stopped { get; init; }

	public int Images { get; init; }

	/// <summary>The storage driver, for example <c>overlay2</c>.</summary>
	public string StorageDriver { get; init; } = "";

	public string RootDirectory { get; init; } = "";

	/// <summary>For example <c>Ubuntu 24.04.1 LTS</c>.</summary>
	public string OperatingSystem { get; init; } = "";

	public string KernelVersion { get; init; } = "";

	public string Architecture { get; init; } = "";

	public int Cpus { get; init; }

	public long MemoryBytes { get; init; }

	/// <summary>The daemon's host name.</summary>
	public string Name { get; init; } = "";

	/// <summary>True when the daemon runs as an ordinary user (rootless mode), which its security options say.</summary>
	public bool Rootless { get; init; }

	/// <summary>Null when the output is not the info object.</summary>
	public static DockerEngineInfo? Parse(string? output)
	{
		JsonElement root = DockerJson.First(output);
		if (root.ValueKind != JsonValueKind.Object || DockerJson.Get(root, "Containers").ValueKind == JsonValueKind.Undefined)
		{
			return null;
		}

		return new DockerEngineInfo
		{
			Containers = DockerJson.Int(root, "Containers") ?? 0,
			Running = DockerJson.Int(root, "ContainersRunning") ?? 0,
			Paused = DockerJson.Int(root, "ContainersPaused") ?? 0,
			Stopped = DockerJson.Int(root, "ContainersStopped") ?? 0,
			Images = DockerJson.Int(root, "Images") ?? 0,
			StorageDriver = DockerJson.Text(root, "Driver"),
			RootDirectory = DockerJson.Text(root, "DockerRootDir"),
			OperatingSystem = DockerJson.Text(root, "OperatingSystem"),
			KernelVersion = DockerJson.Text(root, "KernelVersion"),
			Architecture = DockerJson.Text(root, "Architecture"),
			Cpus = DockerJson.Int(root, "NCPU") ?? 0,
			MemoryBytes = DockerJson.Long(root, "MemTotal") ?? 0,
			Name = DockerJson.Text(root, "Name"),
			Rootless = DockerJson.Strings(root, "SecurityOptions").Any(option => option.Contains("rootless", StringComparison.OrdinalIgnoreCase)),
		};
	}
}
