using System.Diagnostics.CodeAnalysis;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.QuickConnect;

/// <summary>
/// What the quick connect box makes of its text: a protocol and a target to open, or the one line that says what it
/// expected instead.
/// </summary>
/// <remarks>A class and not a record: the target holds a host and a user name, which a record would print.</remarks>
internal sealed class QuickConnectResolution
{
	private QuickConnectResolution(ProtocolDescriptor? protocol, QuickConnectTarget? target, string? problem)
	{
		Protocol = protocol;
		Target = target;
		Problem = problem;
	}

	/// <summary>The protocol the text asked for, which may be known even when its target did not read.</summary>
	public ProtocolDescriptor? Protocol { get; }

	public QuickConnectTarget? Target { get; }

	/// <summary>Why nothing can be opened, in one line, or null when <see cref="IsValid"/>.</summary>
	public string? Problem { get; }

	[MemberNotNullWhen(true, nameof(Protocol), nameof(Target))]
	public bool IsValid => Protocol is not null && Target is not null;

	public static QuickConnectResolution Valid(ProtocolDescriptor protocol, QuickConnectTarget target) =>
		new(protocol, target, null);

	public static QuickConnectResolution Invalid(string problem, ProtocolDescriptor? protocol = null) =>
		new(protocol, null, problem);
}
