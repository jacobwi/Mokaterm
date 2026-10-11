using Mokaterm.Modules.Local.Containers;

namespace Mokaterm.Modules.Local.Tests.Fakes;

/// <summary>Answers every command with one result, or one exception, and remembers what it was asked to run.</summary>
internal sealed class FakeCommandRunner : ILocalCommandRunner
{
	private readonly LocalCommandResult? _result;
	private readonly Exception? _failure;

	public FakeCommandRunner(LocalCommandResult result) => _result = result;

	public FakeCommandRunner(Exception failure) => _failure = failure;

	public string? Program { get; private set; }

	public IReadOnlyList<string> Arguments { get; private set; } = [];

	public TimeSpan Timeout { get; private set; }

	public int OutputLimitBytes { get; private set; }

	public int Calls { get; private set; }

	public Task<LocalCommandResult> RunAsync(
		string program,
		IReadOnlyList<string> arguments,
		TimeSpan timeout,
		int outputLimitBytes,
		CancellationToken cancellationToken)
	{
		Calls++;
		Program = program;
		Arguments = arguments;
		Timeout = timeout;
		OutputLimitBytes = outputLimitBytes;
		return _failure is not null ? Task.FromException<LocalCommandResult>(_failure) : Task.FromResult(_result!);
	}
}
