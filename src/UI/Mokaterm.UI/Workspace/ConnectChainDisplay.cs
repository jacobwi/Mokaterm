using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Icons;
using Moka.Red.Primitives.ChainCheck;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// Turns a session's connect steps into the chain Moka draws: green for a step that held, red where the attempt broke
/// with the failure written under it, grey and dashed for a step still to come, and off for one that never ran.
/// </summary>
internal static class ConnectChainDisplay
{
	public const string RunningWord = "Running";

	public const string SkippedWord = "Not used";

	private const string FallbackFailure = "The connection failed.";

	/// <param name="failed">The attempt ended in a failure; false while it still connects.</param>
	/// <param name="failure">What kind of failure, which gives the failed step its word.</param>
	/// <param name="message">What the session says about the failure, written under the step that failed.</param>
	/// <param name="sessionIcon">The protocol's own icon, for the step that opens the session.</param>
	/// <param name="fix">What puts the failure right, at the end of its row.</param>
	public static IReadOnlyList<MokaChainStep> Steps(
		ConnectStepChain chain,
		bool failed,
		ConnectFailure? failure,
		string? message,
		MokaIconDefinition sessionIcon,
		RenderFragment? fix)
	{
		ArgumentNullException.ThrowIfNull(chain);
		bool broken = failed || chain.Failed is not null;
		List<MokaChainStep> steps = new(chain.Steps.Count);
		foreach (ConnectStepStatus status in chain.Steps)
		{
			MokaIconDefinition icon = IconFor(status.Step.Kind, sessionIcon);
			string label = status.Step.Label;
			steps.Add(status.State switch
			{
				ConnectStepState.Done => new MokaChainStep(icon, label, MokaChainTone.Ok, "", ""),
				ConnectStepState.Running => new MokaChainStep(icon, label, MokaChainTone.Wait, RunningWord, ""),
				ConnectStepState.Skipped => new MokaChainStep(icon, label, MokaChainTone.Off, SkippedWord, ""),
				ConnectStepState.Failed when failed => new MokaChainStep(
					icon,
					label,
					MokaChainTone.Fail,
					FailureWord(failure),
					message ?? status.Detail ?? FallbackFailure,
					Fix: fix),

				// Refused while the attempt goes on, such as a login the user is being asked to correct.
				ConnectStepState.Failed => new MokaChainStep(icon, label, MokaChainTone.Fail, FailureWord(null), status.Detail ?? FallbackFailure),
				_ => new MokaChainStep(icon, label, broken ? MokaChainTone.Off : MokaChainTone.Wait, "", ""),
			});
		}

		return steps;
	}

	public static MokaIconDefinition IconFor(ConnectStepKind kind, MokaIconDefinition sessionIcon) => kind switch
	{
		ConnectStepKind.Route => MokatermIcons.Server,
		ConnectStepKind.Dial => MokatermIcons.Connect,
		ConnectStepKind.Negotiate => MokatermIcons.Transfers,
		ConnectStepKind.Encryption => MokatermIcons.Lock,
		ConnectStepKind.HostIdentity => MokatermIcons.ShieldCheck,
		ConnectStepKind.Login => MokatermIcons.Key,
		ConnectStepKind.Session => sessionIcon,
		_ => MokatermIcons.Globe,
	};

	public static string FailureWord(ConnectFailure? failure) => failure switch
	{
		ConnectFailure.HostUnreachable => "Unreachable",
		ConnectFailure.Timeout => "Timed out",
		ConnectFailure.AuthenticationFailed => "Refused",
		ConnectFailure.HostIdentityRejected => "Not trusted",
		ConnectFailure.Cancelled => "Cancelled",
		_ => "Failed",
	};
}
