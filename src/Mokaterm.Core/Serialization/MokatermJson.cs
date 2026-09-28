using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mokaterm.Core.Serialization;

/// <summary>The serializer options every Core document uses, so plain and encrypted files share one shape.</summary>
internal static class MokatermJson
{
	/// <summary>Indented output for plain documents that people may open in an editor.</summary>
	public static JsonSerializerOptions Document { get; } = Create(indented: true);

	/// <summary>Compact output for encrypted documents, where whitespace only costs bytes.</summary>
	public static JsonSerializerOptions Compact { get; } = Create(indented: false);

	private static JsonSerializerOptions Create(bool indented)
	{
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
		{
			WriteIndented = indented,
			// Computed members such as HostProfile.DisplayName would otherwise be written and ignored on read.
			IgnoreReadOnlyProperties = true,
			// Plain files may be edited by hand.
			ReadCommentHandling = JsonCommentHandling.Skip,
			AllowTrailingCommas = true,
		};
		options.Converters.Add(new JsonStringEnumConverter());
		options.MakeReadOnly(populateMissingResolver: true);
		return options;
	}
}
