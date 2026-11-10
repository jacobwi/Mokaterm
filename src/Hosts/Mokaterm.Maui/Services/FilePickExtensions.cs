namespace Mokaterm.Maui.Services;

// Windows filters a picker by extension only, so a component's accepted types become extensions here. A type with no
// known extensions shows every file instead of hiding some the component would take: the component checks each picked
// file against its own types either way.
internal static class FilePickExtensions
{
	private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
	{
		["image/*"] = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"],
		["video/*"] = [".mp4", ".mov", ".m4v", ".webm", ".mkv", ".avi"],
		["audio/*"] = [".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac"],
		["text/*"] = [".txt", ".csv", ".md", ".log"],
		["text/plain"] = [".txt"],
		["text/csv"] = [".csv"],
		["application/pdf"] = [".pdf"],
		["application/json"] = [".json"],
		["application/zip"] = [".zip"],
	};

	public static IReadOnlyList<string>? For(IReadOnlyList<string> accepted)
	{
		ArgumentNullException.ThrowIfNull(accepted);
		List<string> extensions = [];
		foreach (string token in accepted)
		{
			if (token.Length > 1 && token[0] == '.')
			{
				extensions.Add(token);
			}
			else if (ByType.TryGetValue(token, out string[]? known))
			{
				extensions.AddRange(known);
			}
			else
			{
				return null;
			}
		}

		return extensions.Count == 0 ? null : [.. extensions.Distinct(StringComparer.OrdinalIgnoreCase)];
	}
}
