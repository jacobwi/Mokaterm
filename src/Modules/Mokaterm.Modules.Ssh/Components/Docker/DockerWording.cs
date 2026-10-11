using System.Globalization;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>Sentences more than one page writes the same way.</summary>
internal static class DockerWording
{
	private const int NamesInMessage = 12;

	/// <summary>
	/// The names a confirmation removes: the first dozen written out and the rest counted, while the command shown under
	/// the question names every one of them.
	/// </summary>
	public static string NameList(IReadOnlyList<string> names)
	{
		ArgumentNullException.ThrowIfNull(names);
		string shown = string.Join(", ", names.Take(NamesInMessage));
		return names.Count > NamesInMessage
			? string.Create(CultureInfo.CurrentCulture, $"{shown} and {names.Count - NamesInMessage} more")
			: shown;
	}
}
