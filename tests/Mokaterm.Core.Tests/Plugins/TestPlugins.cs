using System.IO.Compression;
using System.Text;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Core.Tests.Plugins;

/// <summary>Builds plugin packages and folders for tests, without a real plugin assembly.</summary>
internal static class TestPlugins
{
	public static string Sdk => $"{PluginCompatibility.HostSdk.Major}.{PluginCompatibility.HostSdk.Minor}";

	public static string Manifest(string id, string version = "1.0.0", string? assembly = null, string? sdk = null, string? hosts = null)
	{
		List<string> fields = [$"\"id\": \"{id}\"", $"\"name\": \"Plugin {id}\"", $"\"version\": \"{version}\"", "\"publisher\": \"Tests\""];
		if (assembly is not null)
		{
			fields.Add($"\"assembly\": \"{assembly}\"");
			fields.Add($"\"sdk\": \"{sdk ?? Sdk}\"");
		}

		if (hosts is not null)
		{
			fields.Add($"\"hosts\": [{hosts}]");
		}

		return "{ " + string.Join(", ", fields) + " }";
	}

	/// <summary>A theme pack: a manifest and one Windows Terminal scheme.</summary>
	public static Dictionary<string, string> ThemePack(string id, string version = "1.0.0") => new()
	{
		["plugin.json"] = Manifest(id, version),
		["themes/dracula.json"] = Terminal.TerminalThemeSamples.WindowsTerminalDracula,
	};

	/// <summary>A code plugin whose "assembly" is not one, which is enough for everything short of loading it.</summary>
	public static Dictionary<string, string> CodePlugin(string id, string version = "1.0.0", string? sdk = null) => new()
	{
		["plugin.json"] = Manifest(id, version, $"{id}.dll", sdk),
		[$"lib/{id}.dll"] = "not really an assembly " + version,
		["wwwroot/js/plugin.js"] = "export function run() {}",
	};

	public static byte[] Zip(IReadOnlyDictionary<string, string> files)
	{
		using MemoryStream buffer = new();
		using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach ((string path, string text) in files)
			{
				ZipArchiveEntry entry = zip.CreateEntry(path);
				using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
				writer.Write(text);
			}
		}

		return buffer.ToArray();
	}

	/// <summary>Writes the files straight into a plugin folder, the way an operator copies one onto a server.</summary>
	public static void WriteFolder(string folder, IReadOnlyDictionary<string, string> files)
	{
		foreach ((string path, string text) in files)
		{
			string target = Path.Combine(folder, path);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.WriteAllText(target, text);
		}
	}
}
