namespace Mokaterm.Web.Services;

/// <summary>
/// Warns at startup when <c>AllowedHosts</c> lets the server answer for any host name. The server gives shell access to
/// every saved host and has no login of its own, so a page elsewhere that points its own DNS name at this server's
/// address (DNS rebinding) could reach it through a visitor's browser. Naming the hosts the server is reached by makes
/// those requests fail. The default stays "*" so a first run works on any address.
/// </summary>
internal static class AllowedHostsCheck
{
	/// <summary>True for the value ASP.NET Core reads as "any host": missing, empty, or a list holding <c>*</c>.</summary>
	public static bool AllowsAnyHost(string? allowedHosts) =>
		string.IsNullOrWhiteSpace(allowedHosts)
		|| allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("*", StringComparer.Ordinal);

	public static void WarnIfOpen(WebApplication app)
	{
		if (!app.Environment.IsDevelopment() && AllowsAnyHost(app.Configuration["AllowedHosts"]))
		{
			app.Logger.LogWarning(
				"AllowedHosts is \"*\", so this server answers requests for any host name. It gives shell access to every saved "
				+ "host and has no login of its own, so a page that points its own name at this address could reach it through a "
				+ "visitor's browser. Set AllowedHosts to the names this server is reached by.");
		}
	}
}
