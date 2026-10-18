using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A long command whose output is shown while it runs (a build, a push, a compose build): its calls in order, what it is
/// called, the question asked first, and where it is shown.
/// </summary>
/// <param name="Calls">Run one after the other, stopping at the first that fails.</param>
/// <param name="Title">What it is, such as "Build app:1.4": the question's title and the run's heading.</param>
/// <param name="Confirm">The question, shown above the exact commands.</param>
/// <param name="Owner">The page that shows it: <see cref="ImagesOwner"/>, or <see cref="ComposeOwner"/> for a project.</param>
/// <param name="ErrorIsProgress">stderr carries progress, as BuildKit's and compose's do, rather than only errors.</param>
internal sealed record DockerLivePlan(IReadOnlyList<DockerCall> Calls, string Title, string Confirm, string ConfirmText, string Owner, bool ErrorIsProgress)
{
	/// <summary>The owner of builds and pushes started from the images page.</summary>
	public const string ImagesOwner = "images";

	/// <summary>The references whose update check is dropped once the run succeeded: a pushed or rebuilt tag reads anew.</summary>
	public IReadOnlyList<string> Refreshes { get; init; } = [];

	/// <summary>The owner of a compose project's builds.</summary>
	public static string ComposeOwner(string project) => "compose:" + project;

	// The confirmation can quote paths and tags that name the server's layout.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Calls = ").Append(Calls.Count).Append(", ErrorIsProgress = ").Append(ErrorIsProgress);
		return true;
	}
}
