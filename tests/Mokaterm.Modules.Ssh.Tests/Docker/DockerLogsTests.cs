using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Docker's log streams back in order: a container's two streams, and compose's prefixed lines.</summary>
public sealed class DockerLogsTests
{
	[Fact]
	public void Merge_PutsBothStreamsBackInOrder()
	{
		const string Output = "2026-10-11T07:00:01.000000000Z GET /\n2026-10-11T07:00:03.000000000Z GET /cart\n";
		const string Error = "2026-10-11T07:00:02.000000000Z warn: slow\n";

		IReadOnlyList<LogLine> lines = DockerLogParser.Merge(Output, Error);

		Assert.Equal(["GET /", "warn: slow", "GET /cart"], lines.Select(line => line.Text));
		Assert.Equal([false, true, false], lines.Select(line => line.IsError));
		Assert.Equal("2026-10-11T07:00:01.000000000Z", lines[0].Timestamp);
	}

	[Fact]
	public void MergeCompose_TakesTheContainerOffEachLine()
	{
		const string Output = """
			shop-web-1  | 2026-10-11T07:00:02.000000000Z GET / HTTP/1.1
			shop-db-1   | 2026-10-11T07:00:01.000000000Z LOG:  ready | accepting
			shop-web-1  | not stamped
			""";

		IReadOnlyList<LogLine> lines = DockerLogParser.MergeCompose(Output, null);

		Assert.Equal(["shop-web-1", "shop-db-1", "shop-web-1"], lines.Select(line => line.Source));
		Assert.Equal(["not stamped", "LOG:  ready | accepting", "GET / HTTP/1.1"], lines.Select(line => line.Text));
	}
}
