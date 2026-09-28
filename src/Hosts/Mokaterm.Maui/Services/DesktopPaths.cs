namespace Mokaterm.Maui.Services;

/// <summary>
/// Where the desktop app keeps its files, worked out before anything else starts so the crash log and the WebView2
/// profile can use it without the container. On Windows that is %LOCALAPPDATA%\Mokaterm, named here instead of taken
/// from MAUI's FileSystem, which builds the path from the package manifest's publisher and the application id: editing
/// either would have moved the vault.
/// </summary>
internal static class DesktopPaths
{
	/// <summary>
	/// Points the app at another folder: settings, the vault, the logs and the WebView2 profile all move there, and
	/// nothing reads or migrates the usual one. For tests, and for a copy that must not touch the everyday vault. The value
	/// must be an absolute path.
	/// </summary>
	public const string OverrideVariable = "MOKATERM_DATA_DIR";

	/// <summary>
	/// The folder's name under %LOCALAPPDATA%. The Windows installer's package id and title must never equal it:
	/// Velopack's uninstallers delete %LOCALAPPDATA% folders by those names (build/package-windows.ps1 checks).
	/// </summary>
	public const string FolderName = "Mokaterm";

	// Declared before Root: static fields are initialized in this order.
	private static readonly (string? Path, string? Problem) Override = ReadOverride();

	private static readonly Lazy<(string Data, string Cache)> Folders = new(Resolve);

	/// <summary>
	/// The folder this run owns, worked out without touching the disk. The single instance key and the check against
	/// the install folder are made from it before anything is created or moved.
	/// </summary>
	public static string Root { get; } = Override.Path ?? DefaultRoot();

	/// <summary>Why <see cref="OverrideVariable"/> cannot be used, or null. The app must not start while this is set.</summary>
	public static string? OverrideProblem => Override.Problem;

	/// <summary>Settings, the vault and the crash logs.</summary>
	public static string DataDirectory => Folders.Value.Data;

	public static string LogDirectory => Path.Combine(Folders.Value.Data, "logs");

	/// <summary>Scratch files; nothing here has to survive.</summary>
	public static string TempDirectory => Path.Combine(Folders.Value.Cache, "tmp");

	public static string WebViewDirectory => Path.Combine(Folders.Value.Cache, "WebView2");

	private static (string? Path, string? Problem) ReadOverride()
	{
		string? value = Environment.GetEnvironmentVariable(OverrideVariable)?.Trim();
		if (string.IsNullOrEmpty(value))
		{
			return (null, null);
		}

		// A relative path would resolve against the working directory, which for a shortcut is the install folder, and an
		// uninstall deletes that. Falling back to the usual folder instead would open the vault the variable was meant to
		// keep out of reach.
		if (!Path.IsPathFullyQualified(value))
		{
			return (null, $"{OverrideVariable} must be an absolute path, and \"{value}\" is not one.");
		}

		try
		{
			return (Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)), null);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return (null, $"{OverrideVariable} is set to \"{value}\", which is not a usable folder path.");
		}
	}

	private static string DefaultRoot() => OperatingSystem.IsWindows()
		? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName)
		: FileSystem.Current.AppDataDirectory;

	private static (string Data, string Cache) Resolve()
	{
		if (Override.Problem is { } problem)
		{
			throw new InvalidOperationException(problem);
		}

		if (Override.Path is { } root)
		{
			return (Path.Combine(root, "Data"), Path.Combine(root, "Cache"));
		}

		if (!OperatingSystem.IsWindows())
		{
			return (FileSystem.Current.AppDataDirectory, FileSystem.Current.CacheDirectory);
		}

		string windowsRoot = MoveLegacyFolder(Root);
		return (Path.Combine(windowsRoot, "Data"), Path.Combine(windowsRoot, "Cache"));
	}

	private static string MoveLegacyFolder(string root)
	{
		// Up to 0.1.0 the folder came from MAUI's FileSystem, and the manifest still had the template's publisher. One
		// rename on the same volume moves the vault and settings together. If another copy of the app holds files in the
		// old folder, this run keeps using it and the next start tries again.
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string legacyParent = Path.Combine(localAppData, "User Name");
		string legacy = Path.Combine(legacyParent, "com.moka.mokaterm");
		if (Directory.Exists(root) || !Directory.Exists(legacy))
		{
			return root;
		}

		try
		{
			Directory.Move(legacy, root);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return legacy;
		}

		// Every MAUI app that kept the template publisher shares that parent, so it goes only when nothing else is in it.
		try
		{
			Directory.Delete(legacyParent);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}

		return root;
	}
}
