using System.Globalization;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Modules.Ssh.Docker;

// A container's limits and restart policy as the form fills them in, and the docker update they become. The fields
// start from the container's own values; only a field that differs from them goes on the command line, because
// docker update reads a missing flag as "leave it" and has no way to take a limit away.
internal sealed record DockerUpdateSpec
{
	public const string CpusField = "cpus";

	public const string MemoryField = "memory";

	public const string SwapField = "swap";

	public const string RestartField = "restart";

	public required DockerContainerLimits Current { get; init; }

	public string Cpus { get; init; } = "";

	public string Memory { get; init; } = "";

	public string MemorySwap { get; init; } = "";

	public DockerRestartPolicy Restart { get; init; }

	public int MaxRetries { get; init; }

	public static DockerUpdateSpec From(DockerContainerLimits current)
	{
		ArgumentNullException.ThrowIfNull(current);
		return new DockerUpdateSpec
		{
			Current = current,
			Cpus = current.Cpus is { } cpus ? DockerContainerLimits.CpusText(cpus) : "",
			Memory = current.MemoryBytes > 0 ? DockerContainerLimits.SizeText(current.MemoryBytes) : "",
			MemorySwap = current.MemorySwapBytes switch
			{
				-1 => "-1",
				> 0 => DockerContainerLimits.SizeText(current.MemorySwapBytes),
				_ => "",
			},
			Restart = current.RestartPolicy,
			MaxRetries = current.RestartRetries,
		};
	}

	public bool ChangesCpus => Cpus.Trim().Length > 0 && (!DockerNames.TryParseCpus(Cpus, out decimal cpus) || Current.Cpus != cpus);

	public bool ChangesMemory => Memory.Trim().Length > 0 && (!DockerNames.TryParseMemory(Memory, out long bytes) || bytes != Current.MemoryBytes);

	public bool ChangesSwap => MemorySwap.Trim().Length > 0 && SwapBytes() != Current.MemorySwapBytes;

	public bool ChangesRestart => Restart != Current.RestartPolicy
		|| (Restart == DockerRestartPolicy.OnFailure && MaxRetries != Current.RestartRetries);

	public bool HasChanges => ChangesCpus || ChangesMemory || ChangesSwap || ChangesRestart;

	public IReadOnlyDictionary<string, string> Problems(int hostCpus = 0)
	{
		Dictionary<string, string> problems = new(StringComparer.Ordinal);
		string cpus = Cpus.Trim();
		if (cpus.Length == 0 && Current.Cpus is not null)
		{
			problems[CpusField] = "docker update cannot take a CPU limit away. Keep a value, or make the container again without one.";
		}
		else if (ChangesCpus && DockerNames.CpusProblem(cpus, hostCpus) is { } cpuProblem)
		{
			problems[CpusField] = cpuProblem;
		}
		else if (ChangesCpus && Current.HasCpuQuota)
		{
			problems[CpusField] = "This container is capped with --cpu-quota, and docker refuses --cpus beside it. Make it again with --cpus instead.";
		}

		string memory = Memory.Trim();
		if (memory.Length == 0 && Current.MemoryBytes > 0)
		{
			problems[MemoryField] = "docker update cannot take a memory limit away. Keep a value, or make the container again without one.";
		}
		else if (ChangesMemory && DockerNames.MemoryProblem(memory) is { } memoryProblem)
		{
			problems[MemoryField] = memoryProblem;
		}

		CheckSwap(problems);

		if (ChangesRestart && Current.AutoRemove)
		{
			problems[RestartField] = "This container was started with --rm, and docker gives such a container no restart policy.";
		}
		else if (Restart == DockerRestartPolicy.OnFailure && DockerNames.RestartRetriesProblem(MaxRetries) is { } retries)
		{
			problems[RestartField] = retries;
		}

		return problems;
	}

	// The words after docker: only what changes, each as one --flag=value word, so nothing reads as the container's name.
	public IReadOnlyList<string> Arguments(string container, int hostCpus = 0)
	{
		ArgumentNullException.ThrowIfNull(container);
		if (Problems(hostCpus) is { Count: > 0 } problems)
		{
			throw new ArgumentException(problems.Values.First());
		}

		if (!HasChanges)
		{
			throw new ArgumentException("Nothing changes.");
		}

		List<string> words = ["update"];
		if (ChangesCpus)
		{
			words.Add("--cpus=" + Cpus.Trim());
		}

		if (ChangesMemory)
		{
			words.Add("--memory=" + Compact(Memory));
		}

		if (ChangesSwap)
		{
			words.Add("--memory-swap=" + Compact(MemorySwap));
		}

		if (ChangesRestart)
		{
			words.Add("--restart=" + RestartWord());
		}

		words.Add(DockerCommands.Checked(container, DockerNames.ContainerProblem(container)));
		return words;
	}

	public DockerCall ToCall(string container, int hostCpus = 0) =>
		new DockerCall { Program = DockerProgram.Docker, Arguments = Arguments(container, hostCpus) }.AsChange();

	public string RestartWord() => Restart == DockerRestartPolicy.OnFailure && MaxRetries > 0
		? string.Create(CultureInfo.InvariantCulture, $"on-failure:{MaxRetries}")
		: DockerContainerLimits.PolicyWord(Restart);

	private void CheckSwap(Dictionary<string, string> problems)
	{
		string swap = MemorySwap.Trim();
		long memory = ChangesMemory && DockerNames.TryParseMemory(Memory, out long typed) ? typed : Current.MemoryBytes;
		if (swap.Length == 0)
		{
			if (Current.MemorySwapBytes != 0)
			{
				problems[SwapField] = "docker update cannot take a swap limit away. Keep a value, or -1 for no swap limit.";
			}

			return;
		}

		if (!ChangesSwap && !ChangesMemory)
		{
			return;
		}

		if (ChangesSwap && DockerNames.MemorySwapProblem(swap) is { } swapProblem)
		{
			problems[SwapField] = swapProblem;
			return;
		}

		long swapBytes = SwapBytes();
		if (swapBytes != -1 && memory <= 0)
		{
			problems[SwapField] = "A swap limit needs a memory limit: set memory as well.";
		}
		else if (swapBytes != -1 && swapBytes < memory)
		{
			string least = DockerContainerLimits.SizeText(memory);
			problems[SwapField] = ChangesSwap
				? $"The swap limit counts memory and swap together, so it cannot be below the memory limit, {least}."
				: $"The swap limit, {swap}, counts memory and swap together and is below the new memory limit. Raise it to at least {least}, or -1 for no swap limit.";
		}
	}

	private long SwapBytes()
	{
		string swap = MemorySwap.Trim();
		return swap == "-1" ? -1 : DockerNames.TryParseMemory(swap, out long bytes) ? bytes : long.MinValue;
	}

	// docker reads "512 m" too, but one word without the space reads better in the confirmation.
	private static string Compact(string size) => size.Trim().Replace(" ", "", StringComparison.Ordinal);
}
