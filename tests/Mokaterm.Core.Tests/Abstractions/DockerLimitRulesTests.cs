using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Core.Tests.Abstractions;

// What docker update takes for a container's limits: a CPU count and memory sizes, read the way docker reads them.
public sealed class DockerLimitRulesTests
{
	[Theory]
	[InlineData("1")]
	[InlineData("0.5")]
	[InlineData("1.25")]
	[InlineData(" 2 ")]
	[InlineData("0.01")]
	public void Cpus_ACountWithTwoDecimalsAtMost_Passes(string cpus) => Assert.Null(DockerNames.CpusProblem(cpus));

	[Theory]
	[InlineData("", "Name a number")]
	[InlineData("1.125", "two decimals")]
	[InlineData("-1", "two decimals")]
	[InlineData("1,5", "two decimals")]
	[InlineData("1e3", "two decimals")]
	[InlineData("0", "0.01 to 1024")]
	[InlineData("0.001", "two decimals")]
	[InlineData("2000", "0.01 to 1024")]
	public void Cpus_AnythingElse_SaysWhy(string cpus, string expected) =>
		Assert.Contains(expected, DockerNames.CpusProblem(cpus), StringComparison.Ordinal);

	[Fact]
	public void Cpus_AboveTheHostsCount_IsRefusedWithThatCount()
	{
		Assert.Null(DockerNames.CpusProblem("4", hostCpus: 4));
		Assert.Equal("This host has 4 CPUs, so the limit runs from 0.01 to 4.", DockerNames.CpusProblem("4.5", hostCpus: 4));
	}

	[Theory]
	[InlineData("512m", 512L * 1024 * 1024)]
	[InlineData("512M", 512L * 1024 * 1024)]
	[InlineData("1g", 1L << 30)]
	[InlineData("1.5g", 3L << 29)]
	[InlineData("2GiB", 2L << 30)]
	[InlineData("2gb", 2L << 30)]
	[InlineData("64 m", 64L << 20)]
	[InlineData("8388608", 8388608L)]
	[InlineData("1t", 1L << 40)]
	public void Memory_ReadsDockersSizes_In1024s(string text, long bytes)
	{
		Assert.True(DockerNames.TryParseMemory(text, out long parsed));
		Assert.Equal(bytes, parsed);
		Assert.Null(DockerNames.MemoryProblem(text));
	}

	[Theory]
	[InlineData("", "Name a size")]
	[InlineData("lots", "optional unit")]
	[InlineData("512x", "optional unit")]
	[InlineData("-512m", "optional unit")]
	[InlineData("512  m", "optional unit")]
	[InlineData("4m", "below 6m")]
	[InlineData("100000p", "optional unit")]
	public void Memory_AnythingElse_SaysWhy(string memory, string expected) =>
		Assert.Contains(expected, DockerNames.MemoryProblem(memory), StringComparison.Ordinal);

	[Theory]
	[InlineData("-1")]
	[InlineData("1g")]
	[InlineData("6m")]
	public void Swap_ASizeOrMinusOne_Passes(string swap) => Assert.Null(DockerNames.MemorySwapProblem(swap));

	[Theory]
	[InlineData("", "-1 for no swap limit")]
	[InlineData("-2", "a size")]
	[InlineData("unlimited", "a size")]
	public void Swap_AnythingElse_SaysWhy(string swap, string expected) =>
		Assert.Contains(expected, DockerNames.MemorySwapProblem(swap), StringComparison.Ordinal);
}
