using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Tests.Admin;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// Answers detection from a fixture and every other script with exit 0 (or <see cref="Answer"/>), recording what ran,
/// how, and what it was handed on stdin.
/// </summary>
internal sealed class FakeAdminShell : IAdminShell
{
	private bool _root;

	public List<(AdminScript Script, bool Elevated)> Runs { get; } = [];

	/// <summary>The preview of the input each script was handed, or null for none, in the order they ran.</summary>
	public List<string?> Inputs { get; } = [];

	public string DetectOutput { get; init; } = DetectionFixtures.Debian12;

	/// <summary>How a script other than detection ends; null answers exit 0 with no output.</summary>
	public Func<AdminScript, AdminOutcome>? Answer { get; init; }

	public AdminException? FailNext { get; set; }

	public TaskCompletionSource? Gate { get; set; }

	public int RootPrompts { get; private set; }

	public string? ElevationBlockedReason => null;

	public bool HasRoot => _root;

	public string Account => "alice@web1";

	public bool SupportsInput => true;

	public Task EnableRootAsync(CancellationToken cancellationToken)
	{
		RootPrompts++;
		_root = true;
		return Task.CompletedTask;
	}

	public void DisableRoot() => _root = false;

	public async Task<AdminOutcome> RunAsync(AdminScript script, bool elevated, AdminInput? input, CancellationToken cancellationToken)
	{
		lock (Runs)
		{
			Runs.Add((script, elevated));
			Inputs.Add(input?.Preview);
		}

		if (Gate is { } gate)
		{
			await gate.Task.WaitAsync(cancellationToken);
		}

		if (FailNext is { } failure)
		{
			FailNext = null;
			throw failure;
		}

		return script.Name == "detect"
			? new AdminOutcome { ExitCode = 0, Output = DetectOutput.ReplaceLineEndings("\n") }
			: Answer?.Invoke(script) ?? new AdminOutcome { ExitCode = 0 };
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
