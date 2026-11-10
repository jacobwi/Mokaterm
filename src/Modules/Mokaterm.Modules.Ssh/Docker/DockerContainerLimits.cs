using System.Globalization;
using System.Text.Json;

namespace Mokaterm.Modules.Ssh.Docker;

// A container's limits and restart policy from docker inspect's HostConfig, where the daemon writes an unset limit as 0.
internal sealed record DockerContainerLimits
{
	public static readonly DockerContainerLimits None = new();

	// --cpus, in billionths of a CPU.
	public long NanoCpus { get; init; }

	// --cpu-quota and --cpu-period, the older way to cap CPU; docker refuses --cpus while either is set.
	public long CpuQuota { get; init; }

	public long CpuPeriod { get; init; }

	public long MemoryBytes { get; init; }

	// Memory and swap together; -1 for no swap limit. The daemon writes twice the memory when only memory was given.
	public long MemorySwapBytes { get; init; }

	// Started with --rm, which docker will not give a restart policy.
	public bool AutoRemove { get; init; }

	public DockerRestartPolicy RestartPolicy { get; init; }

	public int RestartRetries { get; init; }

	public decimal? Cpus => NanoCpus > 0 ? NanoCpus / 1_000_000_000m : null;

	public bool HasCpuQuota => CpuQuota > 0;

	// The CPUs a quota and period come to, for a container capped the older way.
	public decimal? QuotaCpus => CpuQuota > 0 && CpuPeriod > 0 ? (decimal)CpuQuota / CpuPeriod : null;

	public static DockerContainerLimits Read(JsonElement hostConfig)
	{
		JsonElement policy = DockerJson.Get(hostConfig, "RestartPolicy");
		return new DockerContainerLimits
		{
			NanoCpus = DockerJson.Long(hostConfig, "NanoCpus") ?? 0,
			CpuQuota = DockerJson.Long(hostConfig, "CpuQuota") ?? 0,
			CpuPeriod = DockerJson.Long(hostConfig, "CpuPeriod") ?? 0,
			MemoryBytes = DockerJson.Long(hostConfig, "Memory") ?? 0,
			MemorySwapBytes = DockerJson.Long(hostConfig, "MemorySwap") ?? 0,
			AutoRemove = DockerJson.Bool(hostConfig, "AutoRemove"),
			RestartPolicy = PolicyOf(DockerJson.Text(policy, "Name")),
			RestartRetries = DockerJson.Int(policy, "MaximumRetryCount") ?? 0,
		};
	}

	public static DockerRestartPolicy PolicyOf(string name) => name switch
	{
		"always" => DockerRestartPolicy.Always,
		"unless-stopped" => DockerRestartPolicy.UnlessStopped,
		"on-failure" => DockerRestartPolicy.OnFailure,
		_ => DockerRestartPolicy.No,
	};

	public static string PolicyWord(DockerRestartPolicy policy) => policy switch
	{
		DockerRestartPolicy.Always => "always",
		DockerRestartPolicy.UnlessStopped => "unless-stopped",
		DockerRestartPolicy.OnFailure => "on-failure",
		_ => "no",
	};

	public static string CpusText(decimal cpus) => cpus.ToString("0.##", CultureInfo.InvariantCulture);

	// A byte count the way docker reads one back: the largest 1024 unit it divides into, so 536870912 is 512m.
	public static string SizeText(long bytes)
	{
		if (bytes <= 0)
		{
			return bytes.ToString(CultureInfo.InvariantCulture);
		}

		(long Factor, string Unit)[] units = [(1L << 40, "t"), (1L << 30, "g"), (1L << 20, "m"), (1L << 10, "k")];
		foreach ((long factor, string unit) in units)
		{
			if (bytes % factor == 0)
			{
				return (bytes / factor).ToString(CultureInfo.InvariantCulture) + unit;
			}
		}

		return bytes.ToString(CultureInfo.InvariantCulture);
	}
}
