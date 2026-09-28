using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.Web.Services;

/// <summary>
/// The web host keeps one vault per installation. Set <c>Mokaterm:DataDirectory</c> to move the data out of the
/// content root (recommended in production, and never inside wwwroot).
/// </summary>
internal sealed class WebAppEnvironment : IAppEnvironment
{
	public WebAppEnvironment(IConfiguration configuration, IHostEnvironment hostEnvironment)
	{
		string? configured = configuration["Mokaterm:DataDirectory"];
		DataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Combine(hostEnvironment.ContentRootPath, "App_Data")
			: configured);

		// Inside the data directory rather than the shared system temp folder, where another account on the server could
		// create the folder first. Nothing is created until something needs it.
		TempDirectory = Path.Combine(DataDirectory, "temp");

		OwnerOnlyFiles.CreateDirectory(DataDirectory);

		AppVersion = DisplayVersion.Of(typeof(WebAppEnvironment).Assembly);
	}

	public HostKind Kind => HostKind.Web;

	public string PlatformName => "Web";

	public string AppVersion { get; }

	public string DataDirectory { get; }

	public string TempDirectory { get; }
}
