namespace Mokaterm.Abstractions.Sessions;

/// <summary>
/// Records what a session's terminal shows into a file, one file per session. Scoped like the session manager: a
/// recording belongs to the window or circuit that started it and stops when that goes away.
/// <para>
/// Server output only. A terminal sink never sees what is typed, and nothing here may change that: a log is a plain
/// file, and a password typed at a prompt would be in it in the clear.
/// </para>
/// </summary>
public interface ISessionLogRecorder
{
	/// <summary>Raised when the status of any session changes. Handlers may run on any thread.</summary>
	event Action? Changed;

	/// <summary>The folder the files go in, from the settings or the default under the data directory.</summary>
	string Folder { get; }

	SessionLogStatus StatusFor(Guid sessionId);

	/// <summary>
	/// Starts or stops recording one open session whatever the setting says, so one session can be recorded while the
	/// setting is off and the other way round. Starting again after the size cap opens a new file. A session that is not
	/// open, or has no terminal, is ignored.
	/// </summary>
	void SetRecording(Guid sessionId, bool record);

	/// <summary>The files in <see cref="Folder"/>, newest first. Empty when the folder is missing or cannot be read.</summary>
	IReadOnlyList<SessionLogFile> ListFiles();

	/// <summary>Opens one of the files <see cref="ListFiles"/> returned for reading, alongside a session still writing it.</summary>
	/// <exception cref="ArgumentException"><paramref name="name"/> is not a plain log file name in that folder.</exception>
	/// <exception cref="IOException">The file is gone or cannot be read.</exception>
	Stream OpenRead(string name);
}
