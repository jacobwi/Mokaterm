using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Tests.Shared;

internal sealed record FakeAppEnvironment(string DataDirectory) : IAppEnvironment
{
	public HostKind Kind { get; init; } = HostKind.Desktop;

	public string PlatformName => "Tests";

	public string AppVersion => "0.0.0-test";

	public string TempDirectory => Path.Combine(DataDirectory, "temp");
}
