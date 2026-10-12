using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>
/// An upload parked as the login in a private file of the staging folder, for a step that then moves or copies it where
/// the login cannot write itself: into place as root, or into a container. The file is the login's own, so nobody else
/// reads it meanwhile, and disposing removes whatever is left of it.
/// </summary>
internal sealed class StagedUpload : IAsyncDisposable
{
	private const string NamePrefix = ".mokaterm-upload-";

	private readonly IRemoteFileSystem _login;
	private readonly string _directory;
	private readonly ILogger? _logger;

	private StagedUpload(IRemoteFileSystem login, string directory, ILogger? logger)
	{
		_login = login;
		_directory = directory;
		_logger = logger;
		Path = RemotePath.Combine(directory, NamePrefix + RandomNumberGenerator.GetHexString(32, lowercase: true));
	}

	/// <summary>Where the upload is parked: a new name in the staging folder, used once.</summary>
	public string Path { get; }

	/// <param name="login">The login's own file system, which writes and removes the file.</param>
	/// <param name="directory">The staging folder, an absolute path.</param>
	/// <param name="logger">Hears about a staging file that could not be removed; null keeps that quiet.</param>
	public static StagedUpload In(IRemoteFileSystem login, string directory, ILogger? logger)
	{
		ArgumentNullException.ThrowIfNull(login);
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		return new StagedUpload(login, directory, logger);
	}

	/// <summary>Writes <paramref name="source"/> into the staging file, readable and writable by the login only.</summary>
	/// <param name="target">Where the upload is going, for the message of a failure.</param>
	/// <exception cref="RemoteFileSystemException">The staging folder refused the file; the message says it was the staging that failed.</exception>
	public async Task WriteAsync(Stream source, IProgress<long>? progress, string target, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		try
		{
			UploadOptions options = new() { Permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite };
			await _login.UploadAsync(Path, source, options, progress, cancellationToken);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind is not RemoteFileErrorKind.ConnectionLost)
		{
			throw new RemoteFileSystemException(ex.Kind, $"The upload could not be staged in {_directory}: {ex.Message}", target, ex);
		}
	}

	/// <summary>Removes the staging file, quietly when the step after it already took it away.</summary>
	public async ValueTask DisposeAsync()
	{
		try
		{
			await _login.DeleteAsync(Path, recursive: false, CancellationToken.None);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotFound)
		{
			// Moved into place, or already removed by the script.
		}
		catch (RemoteFileSystemException ex)
		{
			_logger?.LogWarning("A staging file for an upload could not be removed: {Error}", LogSafe.Describe(ex));
		}
	}
}
