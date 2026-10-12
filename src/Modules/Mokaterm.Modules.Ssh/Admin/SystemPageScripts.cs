using Mokaterm.Abstractions.ServerAdmin;

namespace Mokaterm.Modules.Ssh.Admin;

/// <summary>
/// How the public records a System view page from another module hands over become the view's own: a
/// <see cref="SystemScript"/> an <see cref="AdminScript"/>, a <see cref="SystemChange"/> an <see cref="AdminAction"/>,
/// and the outcomes back. Limits are the view's own, so a page cannot ask for more than a built-in one gets.
/// </summary>
internal static class SystemPageScripts
{
	/// <exception cref="ArgumentException">The name is not a script name, or the body is empty or holds NUL.</exception>
	public static AdminScript ToAdminScript(SystemScript script, bool change, bool readsInput)
	{
		ArgumentNullException.ThrowIfNull(script);
		if (!SystemScript.IsName(script.Name))
		{
			throw new ArgumentException("A script is named with letters, digits, dots, dashes and underscores, 64 at most.", nameof(script));
		}

		if (string.IsNullOrWhiteSpace(script.Body) || script.Body.Contains('\0', StringComparison.Ordinal))
		{
			throw new ArgumentException("A script needs a body, and a body cannot hold NUL.", nameof(script));
		}

		TimeSpan timeout = script.Timeout ?? (change ? AdminScript.ActionTimeout : AdminScript.ReadTimeout);
		int output = script.OutputLimitBytes ?? (change ? AdminScript.ActionOutputBytes : AdminScript.ReadOutputBytes);
		return new AdminScript
		{
			Name = script.Name,
			Body = script.Body,
			AsRoot = script.AsRoot,
			Timeout = timeout < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : timeout > AdminScript.PackageTimeout ? AdminScript.PackageTimeout : timeout,
			OutputLimitBytes = Math.Clamp(output, 1, AdminScript.ReadOutputBytes),
			ReadsInput = readsInput,
		};
	}

	/// <exception cref="ArgumentException">The script is not one <see cref="ToAdminScript"/> takes, or the title is empty.</exception>
	public static AdminAction ToAction(SystemChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		ArgumentException.ThrowIfNullOrWhiteSpace(change.Title, nameof(change));
		return new AdminAction
		{
			Title = change.Title,
			Script = ToAdminScript(change.Script, change: true, readsInput: change.Input is not null),
			Description = change.Description,
			Warning = change.Warning,
			CutsConnection = change.CutsConnection,
			Destructive = change.Destructive,
			ConfirmText = string.IsNullOrWhiteSpace(change.ConfirmText) ? "Run" : change.ConfirmText,
		};
	}

	/// <summary>What the change sends on stdin, masked in the review when it carries a preview. The caller disposes it.</summary>
	public static AdminInput? ToInput(SystemChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		return change.Input switch
		{
			null => null,
			{ } text when change.InputPreview is { } preview => AdminInput.Secret(text, preview),
			{ } text => AdminInput.Visible(text),
		};
	}

	public static SystemScriptResult ToResult(AdminOutcome outcome)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		return new SystemScriptResult
		{
			ExitCode = outcome.ExitCode,
			ExitSignal = outcome.ExitSignal,
			Output = outcome.Output,
			Error = outcome.Error,
			IsTruncated = outcome.IsTruncated,
			Duration = outcome.Duration,
			Problem = outcome.Problem(),
		};
	}

	public static SystemChangeResult ToResult(AdminActionResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		return new SystemChangeResult
		{
			Title = result.Title,
			Outcome = result.Outcome is { } outcome ? ToResult(outcome) : null,
			Failure = result.Failure,
		};
	}
}
