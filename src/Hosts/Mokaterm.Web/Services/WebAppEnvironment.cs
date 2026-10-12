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
		DataDirectory = ResolveDataDirectory(configuration, hostEnvironment);

		// Inside the data directory rather than the shared system temp folder, where another account on the server could
		// create the folder first. Nothing is created until something needs it.
		TempDirectory = Path.Combine(DataDirectory, "temp");

		OwnerOnlyFiles.CreateDirectory(DataDirectory);

		AppVersion = DisplayVersion.Of(typeof(WebAppEnvironment).Assembly);
	}

	public HostKind Kind => HostKind.Web;

	/// <summary><c>Mokaterm:DataDirectory</c>, or <c>App_Data</c> under the content root.</summary>
	public static string ResolveDataDirectory(IConfiguration configuration, IHostEnvironment hostEnvironment)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(hostEnvironment);
		string? configured = configuration["Mokaterm:DataDirectory"];
		return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Combine(hostEnvironment.ContentRootPath, "App_Data")
			: configured);
	}

	/// <summary>
	/// <c>Mokaterm:Plugins:Directory</c>, or <c>plugins</c> in the data directory. The operator's folder: whatever is in
	/// it runs on the server, so nothing is ever installed into it through the page.
	/// </summary>
	public static string ResolvePluginsDirectory(IConfiguration configuration, IHostEnvironment hostEnvironment)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		string? configured = configuration["Mokaterm:Plugins:Directory"];
		return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
			? Path.Combine(ResolveDataDirectory(configuration, hostEnvironment), "plugins")
			: configured);
	}

	public string PlatformName => "Web";

	public string AppVersion { get; }

	public string DataDirectory { get; }

	public string TempDirectory { get; }
}
