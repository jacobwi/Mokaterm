#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Mokaterm.Plugin.Sdk;

/// <summary>
/// Writes a plugin's <c>plugin.json</c>. The values come from MSBuild properties, so every one is escaped here rather
/// than pasted into a template, and the hosts are checked by the rule the app reads them with: a package the app would
/// refuse fails the build that made it instead. The targets check the id before anything is staged.
/// </summary>
public sealed class MokatermPluginWriteManifest : Task
{
	[Required]
	public string ManifestPath { get; set; } = "";

	[Required]
	public string Id { get; set; } = "";

	[Required]
	public string Name { get; set; } = "";

	[Required]
	public string PluginVersion { get; set; } = "";

	public string? Publisher { get; set; }

	public string? Description { get; set; }

	public string[]? Hosts { get; set; }

	/// <summary>The assembly file name in lib, or empty for a plugin that carries only data.</summary>
	public string? Assembly { get; set; }

	/// <summary>Mokaterm.Abstractions as the plugin compiled against it, when it references it at all.</summary>
	public ITaskItem[]? ContractsReference { get; set; }

	/// <summary>The Mokaterm version to stamp when nothing is compiled against Mokaterm.Abstractions (a theme pack).</summary>
	public string? FallbackSdkVersion { get; set; }

	[Output]
	public string Sdk { get; set; } = "";

	public override bool Execute()
	{
		string id = Id.Trim();

		List<string> hosts = new List<string>();
		foreach (string host in Hosts ?? Array.Empty<string>())
		{
			string name = host.Trim().ToLowerInvariant();
			if (name.Length == 0)
			{
				continue;
			}

			if (name != "desktop" && name != "web")
			{
				Log.LogError(null, "MOKP003", null, null, 0, 0, 0, 0,
					"MokatermPluginHosts names \"{0}\". A plugin runs on \"desktop\", \"web\" or both; leave it empty for both.", host.Trim());
				return false;
			}

			if (!hosts.Contains(name))
			{
				hosts.Add(name);
			}
		}

		Version? sdk = ReadSdk();
		if (sdk is null)
		{
			Log.LogError(null, "MOKP004", null, null, 0, 0, 0, 0,
				"Cannot tell which Mokaterm version this plugin is for: it does not compile against Mokaterm.Abstractions and MokatermPluginSdkVersion is not a version.");
			return false;
		}

		Sdk = sdk.Major.ToString(CultureInfo.InvariantCulture) + "." + sdk.Minor.ToString(CultureInfo.InvariantCulture);

		StringBuilder json = new StringBuilder();
		json.Append('{').Append('\n');
		List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>
		{
			new KeyValuePair<string, string>("id", id),
			new KeyValuePair<string, string>("name", Name.Trim()),
			new KeyValuePair<string, string>("version", PluginVersion.Trim()),
		};
		AddOptional(fields, "publisher", Publisher);
		AddOptional(fields, "description", Description);
		fields.Add(new KeyValuePair<string, string>("sdk", Sdk));
		AddOptional(fields, "assembly", Assembly);

		for (int i = 0; i < fields.Count; i++)
		{
			json.Append("  ");
			AppendString(json, fields[i].Key);
			json.Append(": ");
			AppendString(json, fields[i].Value);
			if (i < fields.Count - 1 || hosts.Count > 0)
			{
				json.Append(',');
			}

			json.Append('\n');
		}

		if (hosts.Count > 0)
		{
			json.Append("  ");
			AppendString(json, "hosts");
			json.Append(": [");
			for (int i = 0; i < hosts.Count; i++)
			{
				if (i > 0)
				{
					json.Append(", ");
				}

				AppendString(json, hosts[i]);
			}

			json.Append(']').Append('\n');
		}

		json.Append('}').Append('\n');

		string? folder = Path.GetDirectoryName(ManifestPath);
		if (!string.IsNullOrEmpty(folder))
		{
			Directory.CreateDirectory(folder);
		}

		File.WriteAllText(ManifestPath, json.ToString(), new UTF8Encoding(false));
		return !Log.HasLoggedErrors;
	}

	private Version? ReadSdk()
	{
		// The app compares this with the version of its own Mokaterm.Abstractions, so the assembly the plugin compiled
		// against is the answer whenever there is one. Its assembly version follows the package or repository version.
		if (ContractsReference != null && ContractsReference.Length > 0)
		{
			ITaskItem reference = ContractsReference[0];
			string fusionName = reference.GetMetadata("FusionName");
			try
			{
				AssemblyName name = fusionName.Length > 0 ? new AssemblyName(fusionName) : AssemblyName.GetAssemblyName(reference.ItemSpec);
				if (name.Version != null)
				{
					return name.Version;
				}
			}
			catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is ArgumentException || ex is FileLoadException)
			{
				Log.LogMessage(MessageImportance.Low, "Could not read the version of {0}: {1}", reference.ItemSpec, ex.Message);
			}
		}

		string text = (FallbackSdkVersion ?? "").Trim();
		int label = text.IndexOfAny(new[] { '-', '+' });
		if (label >= 0)
		{
			text = text.Substring(0, label);
		}

		return Version.TryParse(text, out Version? parsed) ? parsed : null;
	}

	private static void AddOptional(List<KeyValuePair<string, string>> fields, string key, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			fields.Add(new KeyValuePair<string, string>(key, value!.Trim()));
		}
	}

	private static void AppendString(StringBuilder json, string value)
	{
		const char Backslash = (char)92;
		json.Append('"');
		foreach (char c in value)
		{
			switch (c)
			{
				case '"':
				case Backslash:
					json.Append(Backslash).Append(c);
					break;
				case '\n':
					json.Append(Backslash).Append('n');
					break;
				case '\r':
					json.Append(Backslash).Append('r');
					break;
				case '\t':
					json.Append(Backslash).Append('t');
					break;
				default:
					// Control characters must be escaped in JSON; the two line separators are escaped as well, because a
					// reader that treats the file as script text would otherwise end the line there.
					if (c < (char)0x20 || c == (char)0x2028 || c == (char)0x2029)
					{
						json.Append(Backslash).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					}
					else
					{
						json.Append(c);
					}

					break;
			}
		}

		json.Append('"');
	}
}
