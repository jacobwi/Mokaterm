namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>What a demo docker command printed and exited with.</summary>
internal sealed record DemoDockerResult(int ExitCode, string Output, string Error)
{
	/// <summary>stdout as bytes, for output that is not text (a piece of a file); null sends <see cref="Output"/>.</summary>
	public byte[]? Bytes { get; init; }

	public static DemoDockerResult Ok(string output = "") => new(0, output, "");

	public static DemoDockerResult Fail(int exitCode, string error) => new(exitCode, "", error + "\n");
}
