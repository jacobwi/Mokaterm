using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Core.Keys;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Import;

/// <summary>
/// Finds and reads the key file an imported login names, for the keychain. Desktop only, and decided here because every
/// read goes through this one type: on the web host the home folder and every path a config names are the server's, and
/// a picked config could point at any file there (its own host keys, say), so nothing reads one unless the host is the
/// desktop app.
/// </summary>
internal sealed class IdentityFileReader
{
	private readonly IAppEnvironment _environment;
	private readonly Func<string> _homeDirectory;
	private readonly Func<string, string?> _variable;
	private readonly string _localUser;
	private readonly Func<string> _localHost;

	public IdentityFileReader(IAppEnvironment environment)
		: this(
			environment,
			static () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			Environment.GetEnvironmentVariable,
			Environment.UserName,
			LocalHostName)
	{
	}

	internal IdentityFileReader(
		IAppEnvironment environment,
		Func<string> homeDirectory,
		Func<string, string?> variable,
		string localUser,
		Func<string> localHost)
	{
		_environment = environment;
		_homeDirectory = homeDirectory;
		_variable = variable;
		_localUser = localUser;
		_localHost = localHost;
	}

	/// <summary>True on the desktop, the only host whose files are the user's own.</summary>
	public bool IsAvailable => _environment.Kind == HostKind.Desktop;

	/// <summary>Where the key a login names lives on this machine, or why that cannot be worked out.</summary>
	public IdentityPathResult Locate(ImportedEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		EnsureAvailable();
		if (string.IsNullOrWhiteSpace(entry.IdentityFilePath))
		{
			return IdentityPathResult.Problem("The login names no key file.");
		}

		IdentityPathContext context = new()
		{
			HomeDirectory = _homeDirectory(),
			LocalUser = _localUser,
			LocalHost = _localHost(),
			Variable = _variable,
			Alias = entry.Name,
			HostName = entry.Address,
			RemoteUser = entry.Username,
			Port = entry.Port,
		};

		return IdentityFilePaths.Resolve(entry.IdentityFilePath, entry.IdentityPathStyle, context);
	}

	/// <summary>
	/// Reads the key at <paramref name="fullPath"/>. A missing file comes back as null with no exception; everything
	/// else, the key itself included, comes back in the material, which the caller disposes.
	/// </summary>
	public KeyFileMaterial? Read(string fullPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
		EnsureAvailable();
		try
		{
			if (Directory.Exists(fullPath))
			{
				return KeyFileMaterial.Unreadable("The key path names a folder, not a file.");
			}

			FileInfo file = new(fullPath);
			if (!file.Exists)
			{
				return null;
			}

			if (file.Length > KeyFileImport.MaxFileBytes)
			{
				return KeyFileMaterial.Unreadable("The file is too large to be a private key.");
			}

			using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			long length = stream.Length;
			if (length > KeyFileImport.MaxFileBytes)
			{
				return KeyFileMaterial.Unreadable("The file is too large to be a private key.");
			}

			// Pinned and wiped: an unencrypted key file is the secret itself.
			using PinnedBytes content = new((int)length);
			stream.ReadExactly(content.Span);
			return KeyFileImport.Read(content.Span);
		}
		catch (UnauthorizedAccessException)
		{
			return KeyFileMaterial.Unreadable("This account is not allowed to read the key file.");
		}
		catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
		{
			return KeyFileMaterial.Unreadable("The key file could not be read.");
		}
	}

	private static string LocalHostName()
	{
		try
		{
			return Dns.GetHostName();
		}
		catch (SocketException)
		{
			return Environment.MachineName;
		}
	}

	private void EnsureAvailable()
	{
		if (!IsAvailable)
		{
			throw new InvalidOperationException("Key files are only read in the desktop app.");
		}
	}
}
