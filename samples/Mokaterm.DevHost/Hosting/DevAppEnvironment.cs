using System.Globalization;
using Mokaterm.Abstractions.Platform;

namespace Mokaterm.DevHost.Hosting;

/// <summary>
/// Puts each run's settings, vault and downloads in a new folder under the system temp directory, so every start begins
/// from a freshly seeded vault. Old run folders are left for the OS to clean up.
/// </summary>
internal sealed class DevAppEnvironment : IAppEnvironment
{
	public DevAppEnvironment(TimeProvider timeProvider)
	{
		string run = timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
		DataDirectory = Path.Combine(Path.GetTempPath(), "mokaterm-devhost", run);
		TempDirectory = Path.Combine(DataDirectory, "temp");

		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(TempDirectory);
	}

	public HostKind Kind => HostKind.Web;

	public string PlatformName => "DevHost";

	public string AppVersion { get; } = DisplayVersion.Of(typeof(DevAppEnvironment).Assembly);

	public string DataDirectory { get; }

	public string TempDirectory { get; }
}
