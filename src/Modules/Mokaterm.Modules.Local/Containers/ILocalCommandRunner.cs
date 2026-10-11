namespace Mokaterm.Modules.Local.Containers;

/// <summary>Runs a short command on this computer without a console and returns what it printed.</summary>
internal interface ILocalCommandRunner
{
	/// <param name="program">The full path of the program.</param>
	/// <param name="arguments">Its arguments, each quoted as the platform needs on the way out.</param>
	/// <param name="timeout">How long it may run before it is stopped, its children with it.</param>
	/// <param name="outputLimitBytes">How much standard output is kept; the rest is read and dropped.</param>
	Task<LocalCommandResult> RunAsync(
		string program,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		int outputLimitBytes,
		CancellationToken cancellationToken);
}
