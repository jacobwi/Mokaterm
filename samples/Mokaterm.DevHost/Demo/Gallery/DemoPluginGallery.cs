using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mokaterm.Core.Plugins;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Mokaterm.DevHost.Demo.Gallery;

/// <summary>
/// A plugin gallery for the Browse tab that never leaves this machine. At start it signs a small list with a key made
/// for this run and serves the list, its signature and the packages it names from memory, through the gallery's own
/// HTTP handler, so the real client fetches, verifies and checks hashes exactly as it would against GitHub Pages. It
/// lists every <c>.mokaplugin</c> it finds (the sample plugin once the solution is built), a theme pack made here in
/// two versions with the older one withdrawn, and two plugins with no version for this host, so each state of a card
/// can be looked at.
/// </summary>
internal sealed class DemoPluginGallery
{
	/// <summary>A name that resolves nowhere: only the handler below answers for it.</summary>
	public static Uri Root { get; } = new("https://gallery.devhost.localhost/");

	private const string ThemePackId = "demo.night-themes";

	private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

	private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

	private DemoPluginGallery(string publicKey)
	{
		PublicKey = publicKey;
	}

	public Uri Index { get; } = new(Root, "index.json");

	/// <summary>This run's Ed25519 public key in base64, which the DevHost, and nothing that ships, adds to the trusted keys.</summary>
	public string PublicKey { get; }

	/// <summary>How many packages from disk the list names.</summary>
	public int PackagesFromDisk { get; private set; }

	/// <param name="packagesFolder">A folder of <c>.mokaplugin</c> files to list, or null for the sample plugin's build output.</param>
	/// <param name="contentRoot">The DevHost's content root, from which the sample's output is found.</param>
	public static DemoPluginGallery Create(string? packagesFolder, string contentRoot)
	{
		byte[] secret = RandomNumberGenerator.GetBytes(Ed25519.SecretKeySize);
		byte[] publicKey = new byte[Ed25519.PublicKeySize];
		Ed25519.GeneratePublicKey(secret, 0, publicKey, 0);
		DemoPluginGallery gallery = new(Convert.ToBase64String(publicKey));

		// The format sorts plugins by id.
		SortedDictionary<string, object> plugins = new(StringComparer.Ordinal);
		foreach (string file in FindPackages(packagesFolder, contentRoot))
		{
			if (gallery.FromDisk(file) is { } read)
			{
				plugins[read.Id] = read.Entry;
				gallery.PackagesFromDisk++;
			}
		}

		plugins[ThemePackId] = gallery.ThemePack();
		plugins["demo.hypervisor"] = Unreachable("demo.hypervisor", "Hypervisor", "Guests, storage and backups on a hypervisor host.", "server-tools", ["desktop", "web"], "9.9");
		plugins["demo.serial-tools"] = Unreachable("demo.serial-tools", "Serial tools", "XMODEM and ZMODEM transfers over a serial console.", "protocols", ["desktop"], Sdk);

		byte[] index = JsonSerializer.SerializeToUtf8Bytes(
			new { format = 1, generated = DateTimeOffset.UtcNow, plugins = plugins.Values },
			Json);
		byte[] signature = new byte[Ed25519.SignatureSize];
		Ed25519.Sign(secret, 0, index, 0, index.Length, signature, 0);
		CryptographicOperations.ZeroMemory(secret);

		gallery._files["/index.json"] = index;
		gallery._files["/index.json.sig"] = Encoding.ASCII.GetBytes(Convert.ToBase64String(signature) + "\n");
		return gallery;
	}

	public HttpMessageHandler CreateHandler() => new DemoGalleryHandler(Root.Host, _files);

	private static string Sdk => $"{PluginCompatibility.HostSdk.Major}.{PluginCompatibility.HostSdk.Minor}";

	private static IEnumerable<string> FindPackages(string? packagesFolder, string contentRoot)
	{
		if (!string.IsNullOrWhiteSpace(packagesFolder))
		{
			return Directory.Exists(packagesFolder) ? Directory.EnumerateFiles(packagesFolder, "*.mokaplugin") : [];
		}

		// The newest package the sample plugin's build wrote, whatever the configuration or version.
		string sample = Path.GetFullPath(Path.Combine(contentRoot, "..", "Mokaterm.Plugins.Sample", "bin"));
		return Directory.Exists(sample)
			? Directory.EnumerateFiles(sample, "*.mokaplugin", SearchOption.AllDirectories)
				.OrderByDescending(File.GetLastWriteTimeUtc)
				.Take(1)
			: [];
	}

	private (string Id, object Entry)? FromDisk(string file)
	{
		byte[] package = File.ReadAllBytes(file);
		using ZipArchive zip = new(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
		if (zip.GetEntry("plugin.json") is not { } entry)
		{
			return null;
		}

		using Stream stream = entry.Open();
		using JsonDocument manifest = JsonDocument.Parse(stream);
		JsonElement root = manifest.RootElement;
		string id = root.GetProperty("id").GetString()!;
		string version = root.GetProperty("version").GetString()!;
		bool hasCode = root.TryGetProperty("assembly", out _);
		bool hasThemes = zip.Entries.Any(item => item.FullName.StartsWith("themes/", StringComparison.Ordinal));
		string path = $"/packages/{Path.GetFileName(file)}";
		_files[path] = package;
		return (id, new
		{
			id,
			name = root.GetProperty("name").GetString(),
			publisher = root.TryGetProperty("publisher", out JsonElement publisher) ? publisher.GetString() : "",
			description = root.TryGetProperty("description", out JsonElement description) ? description.GetString() : "",
			categories = hasCode ? (hasThemes ? new[] { "utilities", "server-tools", "themes" } : new[] { "utilities" }) : new[] { "themes" },
			source = "https://github.com/jacobwi/Mokaterm",
			license = "MIT",
			verified = id.StartsWith("moka.", StringComparison.Ordinal),
			hasCode,
			hosts = root.TryGetProperty("hosts", out JsonElement hosts) ? hosts.EnumerateArray().Select(host => host.GetString() ?? "").ToArray() : Array.Empty<string>(),
			versions = new[]
			{
				Version(version, root.TryGetProperty("sdk", out JsonElement sdk) ? sdk.GetString()! : Sdk, path, package, withdrawn: null),
			},
		});
	}

	private object ThemePack()
	{
		const string id = ThemePackId;
		byte[] current = ThemePackage(id, "1.1.0", "#0b0d12");
		byte[] withdrawn = ThemePackage(id, "1.0.0", "#000000");
		_files["/packages/demo.night-themes-1.1.0.mokaplugin"] = current;
		_files["/packages/demo.night-themes-1.0.0.mokaplugin"] = withdrawn;
		return new
		{
			id,
			name = "Night themes",
			publisher = "Demo community",
			description = "Two dark terminal themes for long nights. Data only: nothing in it runs.",
			categories = new[] { "themes" },
			source = "https://github.com/example/night-themes",
			license = "MIT",
			verified = false,
			hasCode = false,
			hosts = Array.Empty<string>(),
			versions = new[]
			{
				Version("1.1.0", Sdk, "/packages/demo.night-themes-1.1.0.mokaplugin", current, withdrawn: null),
				Version("1.0.0", Sdk, "/packages/demo.night-themes-1.0.0.mokaplugin", withdrawn, "Its selection colour hid the cursor. Install 1.1.0 instead."),
			},
		};
	}

	/// <summary>A plugin the list names but this host cannot install, so its download is never asked for.</summary>
	private static object Unreachable(string id, string name, string description, string category, string[] hosts, string sdk) => new
	{
		id,
		name,
		publisher = "Demo community",
		description,
		categories = new[] { category },
		source = $"https://github.com/example/{id}",
		license = "Apache-2.0",
		verified = false,
		hasCode = true,
		hosts,
		versions = new[] { Version("2.0.0", sdk, $"/packages/{id}-2.0.0.mokaplugin", Encoding.ASCII.GetBytes(id), withdrawn: null) },
	};

	private static object Version(string version, string sdk, string path, byte[] package, string? withdrawn) => new
	{
		version,
		sdk,
		url = new Uri(Root, path.TrimStart('/')).AbsoluteUri,
		sha256 = Convert.ToHexStringLower(SHA256.HashData(package)),
		size = package.LongLength,
		commit = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..40],
		published = DateTimeOffset.UtcNow,
		withdrawn,
	};

	private static byte[] ThemePackage(string id, string version, string background)
	{
		using MemoryStream buffer = new();
		using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			Write(zip, "plugin.json", JsonSerializer.Serialize(new
			{
				id,
				name = "Night themes",
				version,
				publisher = "Demo community",
				description = "Two dark terminal themes for long nights. Data only: nothing in it runs.",
			}, Json));
			Write(zip, "themes/midnight.json", Scheme("Midnight", background, "#c9d1d9", "#58a6ff"));
			Write(zip, "themes/ember.json", Scheme("Ember", background, "#e8e8ec", "#ef5350"));
		}

		return buffer.ToArray();
	}

	private static string Scheme(string name, string background, string foreground, string accent) => JsonSerializer.Serialize(new
	{
		name,
		black = "#21222c",
		red = "#ef5350",
		green = "#00e676",
		yellow = "#ffd54f",
		blue = accent,
		purple = "#ce93d8",
		cyan = "#4dd0e1",
		white = foreground,
		brightBlack = "#6a6a74",
		brightRed = "#ff6b68",
		brightGreen = "#69f0ae",
		brightYellow = "#ffe082",
		brightBlue = accent,
		brightPurple = "#e1bee7",
		brightCyan = "#84ffff",
		brightWhite = "#ffffff",
		background,
		foreground,
		cursorColor = accent,
		selectionBackground = "#1a1a22",
	}, Json);

	private static void Write(ZipArchive zip, string path, string text)
	{
		using StreamWriter writer = new(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
		writer.Write(text);
	}
}
