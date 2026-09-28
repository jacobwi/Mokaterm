using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads a file this app exported. There is nothing to find on the machine itself, so it only ever works on a picked
/// file, which is also why it says so instead of looking anywhere.
/// </summary>
internal sealed class ConnectionBundleImportSource : IConnectionImportSource
{
	public const string SourceId = "mokaterm";

	private const int MaxFileBytes = 16 * 1024 * 1024;

	public ImportSourceInfo Info { get; } = new()
	{
		Id = SourceId,
		DisplayName = "Mokaterm export",
		Description = "A connections file this app wrote",
		Kind = ImportSourceKind.File,
		AcceptsFile = true,
		FileExtensions = [".json"],
	};

	public ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.File is not { } file)
		{
			return ValueTask.FromResult(ImportPreview.Nothing(SourceId, "Pick a file this app exported."));
		}

		if (file.Content.Length > MaxFileBytes)
		{
			return ValueTask.FromResult(ImportPreview.Failed(
				SourceId,
				string.Create(CultureInfo.CurrentCulture, $"{file.Name} is larger than {MaxFileBytes / (1024 * 1024)} MB."),
				file.Name));
		}

		ConnectionBundle? bundle;
		try
		{
			bundle = JsonSerializer.Deserialize<ConnectionBundle>(file.Content, MokatermJson.Document);
		}
		catch (JsonException)
		{
			return ValueTask.FromResult(ImportPreview.Failed(SourceId, $"{file.Name} is not readable JSON.", file.Name));
		}

		if (bundle is null || !string.Equals(bundle.Kind, ConnectionBundle.BundleKind, StringComparison.Ordinal))
		{
			return ValueTask.FromResult(ImportPreview.Failed(SourceId, $"{file.Name} is not a Mokaterm export.", file.Name));
		}

		if (bundle.Version > ConnectionBundle.CurrentVersion)
		{
			return ValueTask.FromResult(ImportPreview.Failed(
				SourceId,
				$"{file.Name} comes from a newer version of Mokaterm.",
				file.Name));
		}

		List<ImportedEntry> entries = [];
		List<ImportSkip> skipped = [];
		int index = 0;
		foreach (ConnectionBundleEntry entry in bundle.Entries)
		{
			index++;
			if (string.IsNullOrWhiteSpace(entry.Address) || string.IsNullOrWhiteSpace(entry.ProtocolId))
			{
				skipped.Add(new ImportSkip(string.IsNullOrWhiteSpace(entry.Name) ? $"Entry {index}" : entry.Name, "No address or protocol."));
				continue;
			}

			entries.Add(new ImportedEntry
			{
				Key = string.Create(CultureInfo.InvariantCulture, $"{index}:{entry.Address}"),
				Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Address : entry.Name,
				Address = entry.Address,
				ProtocolId = entry.ProtocolId,
				Port = entry.Port,
				Username = entry.Username,
				AuthenticationMethod = entry.AuthenticationMethod,
				Options = entry.Options,
				FolderPath = entry.FolderPath,
				Notes = entry.Notes,
				Tags = entry.Tags,
				Environment = entry.Environment,
				Color = entry.Color,
				Label = entry.Label,
			});
		}

		return ValueTask.FromResult(new ImportPreview
		{
			SourceId = SourceId,
			Availability = entries.Count > 0 ? ImportAvailability.Found : ImportAvailability.NotFound,
			Location = file.Name,
			Message = entries.Count > 0 ? null : "The file holds no hosts.",
			Entries = entries,
			Skipped = skipped,
		});
	}
}
