using System.Globalization;
using Mokaterm.Abstractions.Containers;

namespace Mokaterm.DevHost.Demo.Docker;

// docker update as the daemon takes it: each flag as one --flag=value word, a missing flag left as it is, and the
// daemon's own refusals for a memory limit above the swap limit and a --cpus above the host's CPUs.
internal sealed partial class DemoDockerEngine
{
	private const int DemoCpus = 4;

	private DemoDockerResult Update(string[] words)
	{
		if (words.Length == 0 || words[^1].StartsWith('-') || Find(words[^1]) is not { } container)
		{
			return DemoDockerResult.Fail(1, $"Error response from daemon: No such container: {(words.Length > 0 ? words[^1] : "")}");
		}

		long? cpus = null;
		long? memory = null;
		long? swap = null;
		string? restart = null;
		foreach (string word in words[..^1])
		{
			(string flag, string value) = word.IndexOf('=', StringComparison.Ordinal) is var equals and > 0 ? (word[..equals], word[(equals + 1)..]) : (word, "");
			switch (flag)
			{
				case "--cpus" when decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal count):
					cpus = (long)(count * 1_000_000_000m);
					break;
				case "--memory" when DockerNames.TryParseMemory(value, out long bytes):
					memory = bytes;
					break;
				case "--memory-swap" when value == "-1" || DockerNames.TryParseMemory(value, out _):
					swap = value == "-1" ? -1 : DockerNames.TryParseMemory(value, out long swapBytes) ? swapBytes : 0;
					break;
				case "--restart":
					restart = value;
					break;
				default:
					return DemoDockerResult.Fail(125, $"invalid argument \"{word}\" for docker update\nSee 'docker update --help'.");
			}
		}

		string refusal = $"Error response from daemon: Cannot update container {container.Id}: ";
		if (cpus is > DemoCpus * 1_000_000_000L)
		{
			return DemoDockerResult.Fail(1, refusal + string.Create(CultureInfo.InvariantCulture, $"Range of CPUs is from 0.01 to {DemoCpus}.00, as there are only {DemoCpus} CPUs available"));
		}

		long currentSwap = container.MemorySwap;
		if (memory is { } limit && swap is null && currentSwap > 0 && limit > currentSwap)
		{
			return DemoDockerResult.Fail(1, refusal + "Memory limit should be smaller than already set memoryswap limit, update the memoryswap at the same time");
		}

		if (cpus is { } nano)
		{
			container.NanoCpus = nano;
		}

		if (memory is { } bytesLimit)
		{
			container.MemoryLimit = bytesLimit;
			if (swap is null && container.MemorySwap == 0)
			{
				container.MemorySwap = bytesLimit * 2;
			}
		}

		if (swap is { } swapLimit)
		{
			container.MemorySwap = swapLimit;
		}

		if (restart is not null)
		{
			SetRestartPolicy(container, restart);
		}

		return DemoDockerResult.Ok(words[^1] + "\n");
	}
}
