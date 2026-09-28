using System.Buffers;
using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Platform;
using Mokaterm.DevHost.Demo;

namespace Mokaterm.DevHost.Hosting;

/// <summary>
/// Local files without real dialogs. Picking returns generated files that stream deterministic bytes, and saving writes
/// into the run's data directory, so uploads and downloads can be tried with nothing on disk and no browser prompts.
/// </summary>
internal sealed class DemoLocalFileAccess : ILocalFileAccess
{
	private const int KiB = 1024;
	private const int MiB = 1024 * 1024;
	private const int SaveBufferSize = 81920;

	private static readonly DateTimeOffset PickedModified = new(2026, 9, 14, 16, 20, 0, TimeSpan.Zero);

	private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create(Path.GetInvalidFileNameChars());

	private static readonly byte[] ReleaseNotes = Utf8("""
		Release 2.4.1

		- Faster directory listings on slow links
		- Transfer queue keeps its order after a reconnect
		- Fixed a crash when a symbolic link points at itself

		""");

	private static readonly byte[] IndexHtml = Utf8("""
		<!DOCTYPE html>
		<html lang="en">
		<head>
			<meta charset="utf-8">
			<title>Status</title>
			<link rel="stylesheet" href="css/app.css">
		</head>
		<body>
			<img src="img/logo.svg" alt="">
			<h1>All systems normal</h1>
			<script src="js/vendor/htmx.min.js"></script>
			<script src="js/app.js"></script>
		</body>
		</html>

		""");

	private static readonly byte[] AppCss = Utf8("""
		:root { color-scheme: dark; --accent: #ef5350; }
		body { margin: 0; font: 14px/1.5 Inter, sans-serif; background: #060608; color: #e8e8ec; }
		h1 { font-size: 17px; letter-spacing: -0.01em; }

		""");

	private static readonly byte[] AppJs = Utf8("""
		document.addEventListener('htmx:afterSwap', event => console.debug('swapped', event.detail.target.id));

		""");

	private static readonly byte[] LogoSvg = Utf8("""
		<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 17l6-5-6-5M12 19h8" fill="none" stroke="#ef5350" stroke-width="2"/></svg>

		""");

	private readonly IAppEnvironment _environment;

	public DemoLocalFileAccess(IAppEnvironment environment) => _environment = environment;

	public bool CanPickFolders => true;

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default)
	{
		LocalFileItem notes = Generated("release-notes.txt", 4 * KiB, ReleaseNotes);
		IReadOnlyList<LocalFileItem> files = multiple
			? [notes, Generated("firmware-2.4.1.bin", 25 * MiB), Generated("site-backup.tar.gz", 180 * MiB)]
			: [notes];
		return ValueTask.FromResult(files);
	}

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<LocalFileItem> items =
		[
			Folder("site"),
			Generated("site/index.html", IndexHtml.Length, IndexHtml),
			Folder("site/css"),
			Generated("site/css/app.css", AppCss.Length, AppCss),
			Folder("site/img"),
			Generated("site/img/hero.jpg", 1400 * KiB),
			Generated("site/img/logo.svg", LogoSvg.Length, LogoSvg),
			Folder("site/js"),
			Generated("site/js/app.js", 12 * KiB, AppJs),
			Folder("site/js/vendor"),
			Generated("site/js/vendor/htmx.min.js", 47 * KiB, AppJs),
		];
		return ValueTask.FromResult(items);
	}

	public async ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		string directory = Path.Combine(_environment.DataDirectory, "downloads");
		Directory.CreateDirectory(directory);
		string path = AvailablePath(directory, SafeFileName(suggestedName));

		bool saved = false;
		FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, SaveBufferSize, useAsync: true);
		try
		{
			await using (stream)
			{
				await writeAsync(stream, cancellationToken);
			}

			saved = true;
		}
		finally
		{
			if (!saved)
			{
				// A cancelled or failed download must not look like a finished file.
				TryDelete(path);
			}
		}

		return true;
	}

	private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"));

	private static LocalFileItem Folder(string relativePath) => new()
	{
		Name = NameOf(relativePath),
		RelativePath = relativePath,
		IsDirectory = true,
		LastModified = PickedModified,
	};

	private static LocalFileItem Generated(string relativePath, long length, byte[]? seed = null) => new()
	{
		Name = NameOf(relativePath),
		RelativePath = relativePath,
		Length = length,
		LastModified = PickedModified,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new GeneratedStream(length, seed)),
	};

	private static string NameOf(string relativePath) => relativePath[(relativePath.LastIndexOf('/') + 1)..];

	private static string SafeFileName(string suggestedName)
	{
		string name = Path.GetFileName(suggestedName);
		string cleaned = string.Create(name.Length, name, static (span, source) =>
		{
			for (int i = 0; i < source.Length; i++)
			{
				span[i] = InvalidNameChars.Contains(source[i]) ? '_' : source[i];
			}
		});

		return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
	}

	private static string AvailablePath(string directory, string fileName)
	{
		string stem = Path.GetFileNameWithoutExtension(fileName);
		string extension = Path.GetExtension(fileName);
		string path = Path.Combine(directory, fileName);
		for (int copy = 2; File.Exists(path); copy++)
		{
			path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{stem} ({copy}){extension}"));
		}

		return path;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The partial file stays; the transfer already reports the failure that matters.
		}
	}
}
