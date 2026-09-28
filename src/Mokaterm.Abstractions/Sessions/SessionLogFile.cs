namespace Mokaterm.Abstractions.Sessions;

/// <summary>A session log file already on disk.</summary>
public sealed record SessionLogFile
{
	/// <summary>The file name without its folder, which is what <see cref="ISessionLogRecorder.OpenRead"/> takes.</summary>
	public required string Name { get; init; }

	public required long Length { get; init; }

	public required DateTimeOffset WrittenAt { get; init; }
}
