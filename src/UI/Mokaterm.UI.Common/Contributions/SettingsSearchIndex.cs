using System.Collections.Concurrent;
using System.Reflection;

namespace Mokaterm.UI.Common.Contributions;

/// <summary>
/// The rows of every settings page, read from the <see cref="SettingsSearchEntryAttribute"/>s the source generator
/// put on the page's assembly. A row whose label is computed is not in it; its page's keywords are what finds it.
/// </summary>
public static class SettingsSearchIndex
{
	// Assemblies never unload here, and reading their attributes is the costly part, so each is read once.
	private static readonly ConcurrentDictionary<Assembly, ILookup<Type, SettingsSearchEntry>> Assemblies = new();

	/// <summary>The rows of <paramref name="page"/>, in the order the page draws them.</summary>
	public static IReadOnlyList<SettingsSearchEntry> For(Type page)
	{
		ArgumentNullException.ThrowIfNull(page);
		return [.. Assemblies.GetOrAdd(page.Assembly, Read)[page]];
	}

	private static ILookup<Type, SettingsSearchEntry> Read(Assembly assembly) =>
		assembly.GetCustomAttributes<SettingsSearchEntryAttribute>()
			.ToLookup(entry => entry.Page, entry => new SettingsSearchEntry(entry.Section, entry.Label, entry.Description));
}
