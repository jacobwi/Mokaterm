using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>A local file access that saves into memory and lists what it saved, for transfers driven by the tests.</summary>
internal sealed class FakeLocalFileAccess : ILocalFileAccess
{
	public bool CanPickFolders => false;

	public List<(string Name, byte[] Bytes)> Saved { get; } = [];

	/// <summary>When set, the next save is declined the way a canceled dialog declines it.</summary>
	public bool DeclineNext { get; set; }

	public List<string> PickedNames { get; private set; } = [];

	public List<byte[]> Picked { get; } = [];

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
	{
		List<LocalFileItem> items = [.. Picked.Select((bytes, index) => new LocalFileItem
		{
			Name = PickedNames.Count > index ? PickedNames[index] : $"picked-{index}",
			RelativePath = PickedNames.Count > index ? PickedNames[index] : $"picked-{index}",
			Length = bytes.Length,
			OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
		})];
		return ValueTask.FromResult<IReadOnlyList<LocalFileItem>>(items);
	}

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyList<LocalFileItem>>([]);

	public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		if (DeclineNext)
		{
			DeclineNext = false;
			return ValueTask.FromResult(false);
		}

		MemoryStream saved = new();
		return SaveAsync(saved, suggestedName, writeAsync, cancellationToken);
	}

	private async ValueTask<bool> SaveAsync(MemoryStream saved, string suggestedName, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken)
	{
		await writeAsync(saved, cancellationToken);
		Saved.Add((suggestedName, [.. saved.ToArray()]));
		return true;
	}
}
