namespace Mokaterm.Abstractions.Platform;

public enum HostKind
{
	/// <summary>MAUI Blazor Hybrid: one user, local files, native dialogs.</summary>
	Desktop,

	/// <summary>Blazor Server: connections run on the server, files come and go through the browser.</summary>
	Web,
}

/// <summary>Facts about the running host. Registered by each host project.</summary>
public interface IAppEnvironment
{
	HostKind Kind { get; }

	/// <summary>For display, for example "Windows" or "Web".</summary>
	string PlatformName { get; }

	string AppVersion { get; }

	/// <summary>Holds settings.json, the vault header and encrypted documents. Created on startup.</summary>
	string DataDirectory { get; }

	/// <summary>
	/// Scratch space that may be wiped between runs. It may not exist yet, so whatever writes there creates it first.
	/// </summary>
	string TempDirectory { get; }
}
