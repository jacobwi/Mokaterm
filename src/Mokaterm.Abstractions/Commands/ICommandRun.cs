namespace Mokaterm.Abstractions.Commands;

/// <summary>One command running on several saved logins, each over a connection of its own that no tab shows.</summary>
public interface ICommandRun : IHostRun<CommandHostResult>
{
	/// <summary>The command as it was sent. It lives in memory only, for as long as the run does.</summary>
	string Command { get; }
}
