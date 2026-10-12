#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Mokaterm.Plugin.Sdk;

/// <summary>
/// Sorts the files the build copies next to a plugin into the ones the host provides and the ones the plugin carries.
/// The host already has everything that reaches the plugin through its contracts (Mokaterm.Abstractions,
/// Mokaterm.UI.Common) and loads its own copy of each whatever the plugin brings, so a second copy would only make the
/// package bigger and have the user trust files that never run. That set is read from the restore graph in
/// project.assets.json: the host roots and everything they depend on. A file the graph cannot place falls back to the
/// names the host is known to ship.
/// </summary>
public sealed class MokatermPluginSelectLibFiles : Task
{
	private static readonly string[] AssetSections = { "compile", "runtime", "native", "resource", "runtimeTargets" };

	/// <summary>The copy-local files of the build: package assets, project references and their satellites.</summary>
	public ITaskItem[]? Candidates { get; set; }

	public string? AssetsFile { get; set; }

	public string? TargetFramework { get; set; }

	[Required]
	public string[] HostRoots { get; set; } = Array.Empty<string>();

	/// <summary>Assembly name prefixes the host ships, for files the restore graph does not account for.</summary>
	public string[]? HostAssemblyPrefixes { get; set; }

	/// <summary>The candidates the plugin carries in its lib folder, each with PackagePath set under lib/.</summary>
	[Output]
	public ITaskItem[] PrivateFiles { get; set; } = Array.Empty<ITaskItem>();

	/// <summary>The candidates the host provides, as they were passed in.</summary>
	[Output]
	public ITaskItem[] HostFiles { get; set; } = Array.Empty<ITaskItem>();

	public override bool Execute()
	{
		HashSet<string> hostLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> hostAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool haveGraph = !string.IsNullOrEmpty(AssetsFile) && File.Exists(AssetsFile) && ReadHostGraph(AssetsFile!, hostLibraries, hostAssets);

		List<ITaskItem> selected = new List<ITaskItem>();
		List<ITaskItem> host = new List<ITaskItem>();
		HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ITaskItem candidate in Candidates ?? Array.Empty<ITaskItem>())
		{
			string fileName = Path.GetFileName(candidate.ItemSpec);
			string? reason = HostReason(candidate, fileName, haveGraph, hostLibraries, hostAssets);
			if (reason != null)
			{
				Log.LogMessage(MessageImportance.Low, "{0} is the host's: {1}.", fileName, reason);
				host.Add(candidate);
				continue;
			}

			// Documentation files ride along with references and are never read at run time.
			if (string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string subFolder = candidate.GetMetadata("DestinationSubDirectory").Replace('\\', '/').Trim('/');
			string packagePath = "lib/" + (subFolder.Length > 0 ? subFolder + "/" : "") + fileName;
			if (!paths.Add(packagePath))
			{
				continue;
			}

			TaskItem item = new TaskItem(candidate.ItemSpec);
			item.SetMetadata("PackagePath", packagePath);
			selected.Add(item);
		}

		PrivateFiles = selected.ToArray();
		HostFiles = host.ToArray();
		return !Log.HasLoggedErrors;
	}

	private string? HostReason(ITaskItem candidate, string fileName, bool haveGraph, HashSet<string> hostLibraries, HashSet<string> hostAssets)
	{
		string origin = Origin(candidate);
		if (haveGraph)
		{
			if (origin.Length > 0 && hostLibraries.Contains(origin))
			{
				return "it comes from " + origin + ", which the host provides";
			}

			// A file found next to a project reference rather than restored from a package carries the project as its
			// origin, so the assembly's own name is checked against the host's as well.
			if (hostAssets.Contains(BaseName(fileName)))
			{
				return "the host provides an assembly of that name";
			}

			if (origin.Length > 0)
			{
				return null;
			}
		}

		string name = BaseName(fileName);
		foreach (string prefix in HostAssemblyPrefixes ?? Array.Empty<string>())
		{
			string trimmed = prefix.Trim();
			if (trimmed.Length > 0 && name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
			{
				Log.LogMessage(MessageImportance.High,
					"{0} is left out of the plugin: nothing says where it came from, and the host ships assemblies named {1}*.",
					fileName, trimmed);
				return "it is named like an assembly the host ships";
			}
		}

		return null;
	}

	private static string Origin(ITaskItem candidate)
	{
		string package = candidate.GetMetadata("NuGetPackageId");
		if (package.Length > 0)
		{
			return package;
		}

		string project = candidate.GetMetadata("MSBuildSourceProjectFile");
		return project.Length > 0 ? Path.GetFileNameWithoutExtension(project) : "";
	}

	// "Moka.Red.dll" and "Moka.Red.pdb" are the same assembly, and "de/Moka.Red.resources.dll" is its satellite.
	private static string BaseName(string fileName) => Path.GetFileNameWithoutExtension(fileName);

	private bool ReadHostGraph(string assetsFile, HashSet<string> hostLibraries, HashSet<string> hostAssets)
	{
		XElement root;
		try
		{
			// netstandard has no JSON reader of its own that every MSBuild can load, but it has this mapping onto XML.
			byte[] bytes = File.ReadAllBytes(assetsFile);
			using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
			{
				root = XElement.Load(reader);
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
		{
			Log.LogMessage(MessageImportance.High, "Could not read {0} ({1}); host assemblies are recognised by name only.", assetsFile, ex.Message);
			return false;
		}

		List<XElement> targets = PickTargets(root);
		if (targets.Count == 0)
		{
			return false;
		}

		Dictionary<string, List<XElement>> libraries = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
		foreach (XElement target in targets)
		{
			foreach (XElement library in target.Elements())
			{
				string key = Key(library);
				int slash = key.IndexOf('/');
				string name = slash < 0 ? key : key.Substring(0, slash);
				if (!libraries.TryGetValue(name, out List<XElement>? entries))
				{
					entries = new List<XElement>();
					libraries.Add(name, entries);
				}

				entries.Add(library);
			}
		}

		Queue<string> pending = new Queue<string>(HostRoots.Select(root => root.Trim()).Where(root => root.Length > 0));
		while (pending.Count > 0)
		{
			string name = pending.Dequeue();
			if (!hostLibraries.Add(name) || !libraries.TryGetValue(name, out List<XElement>? entries))
			{
				continue;
			}

			foreach (XElement library in entries)
			{
				foreach (XElement dependency in Children(library, "dependencies"))
				{
					pending.Enqueue(Key(dependency));
				}

				foreach (string section in AssetSections)
				{
					foreach (XElement asset in Children(library, section))
					{
						string assetName = BaseName(Key(asset).Replace('\\', '/').Split('/').Last());
						if (assetName != "_")
						{
							hostAssets.Add(assetName);
						}
					}
				}
			}
		}

		return true;
	}

	// The graph for this target framework, or for every framework the project restored when the names do not line up
	// (an alias): a host package for another framework is still a host package.
	private List<XElement> PickTargets(XElement root)
	{
		List<XElement> all = Children(root, "targets").Where(target => Key(target).IndexOf('/') < 0).ToList();
		string framework = TargetFramework ?? "";
		string mapped = Children(Member(root, "project"), "frameworks")
			.Where(entry => string.Equals(Key(entry), framework, StringComparison.OrdinalIgnoreCase))
			.Select(entry => Member(entry, "framework")?.Value)
			.FirstOrDefault() ?? "";
		List<XElement> matching = all
			.Where(target => string.Equals(Key(target), framework, StringComparison.OrdinalIgnoreCase)
				|| (mapped.Length > 0 && string.Equals(Key(target), mapped, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		return matching.Count > 0 ? matching : all;
	}

	private static XElement? Member(XElement? parent, string key) => parent?.Elements().FirstOrDefault(element => Key(element) == key);

	private static IEnumerable<XElement> Children(XElement? parent, string key) => Member(parent, key)?.Elements() ?? Enumerable.Empty<XElement>();

	// The mapping keeps a JSON name that is a valid XML name as the element's name, and puts any other in an "item"
	// attribute: "net10.0" stays an element name, "Moka.Red/0.1.17" does not.
	private static string Key(XElement element)
	{
		XAttribute? item = element.Attribute("item");
		return item != null ? item.Value : element.Name.LocalName;
	}
}
