using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What the last edit of a compose project's file came to, as its tab shows it until dismissed or the next edit. A class
/// and not a record, so nothing prints compose's output, which can quote a value from an env file.
/// </summary>
internal sealed class ProjectFileEdit
{
	private ProjectFileEdit(string path, bool asRoot, bool createNew, bool saved, bool checkedByCompose, string? problem, string? output, bool offerRoot)
	{
		Path = path;
		AsRoot = asRoot;
		CreateNew = createNew;
		Saved = saved;
		CheckedByCompose = checkedByCompose;
		Problem = problem;
		Output = output;
		OfferRoot = offerRoot;
	}

	public string Path { get; }

	public string Name => DockerHostPaths.NameOf(Path);

	/// <summary>True when it was opened through the session's root file system.</summary>
	public bool AsRoot { get; }

	/// <summary>True when the edit was to create the file.</summary>
	public bool CreateNew { get; }

	/// <summary>True once a save went through.</summary>
	public bool Saved { get; }

	/// <summary>True when compose read the project's files again after the save.</summary>
	public bool CheckedByCompose { get; }

	/// <summary>Why the file could not be opened or, after a save, why compose no longer takes the project's files.</summary>
	public string? Problem { get; }

	/// <summary>Everything compose printed when it refused the files.</summary>
	public string? Output { get; }

	/// <summary>The login may not open the file, and root may.</summary>
	public bool OfferRoot { get; }

	/// <summary>The file could not be opened or created; nothing was written.</summary>
	public static ProjectFileEdit Failure(string path, bool asRoot, bool createNew, string problem, bool offerRoot) =>
		new(path, asRoot, createNew, saved: false, checkedByCompose: false, problem, output: null, offerRoot);

	/// <summary>Saved, and compose either took the files again or was not there to ask.</summary>
	public static ProjectFileEdit Written(string path, bool asRoot, bool checkedByCompose) =>
		new(path, asRoot, createNew: false, saved: true, checkedByCompose, problem: null, output: null, offerRoot: false);

	/// <summary>Saved, and compose refused the project's files afterwards.</summary>
	public static ProjectFileEdit Refused(string path, bool asRoot, string problem, string? output) =>
		new(path, asRoot, createNew: false, saved: true, checkedByCompose: true, problem, output, offerRoot: false);
}
