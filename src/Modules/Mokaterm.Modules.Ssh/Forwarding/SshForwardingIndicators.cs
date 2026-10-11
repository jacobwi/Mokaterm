using System.Globalization;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>Agent and X11 forwarding as the chips the shell's status bar shows for the focused session.</summary>
internal sealed class SshForwardingIndicators : ISessionIndicators
{
	private readonly ISshForwardingFeature _forwarding;

	public SshForwardingIndicators(ISshForwardingFeature forwarding) => _forwarding = forwarding;

	public event Action? Changed
	{
		add => _forwarding.Changed += value;
		remove => _forwarding.Changed -= value;
	}

	public IReadOnlyList<SessionIndicator> Indicators
	{
		get
		{
			List<SessionIndicator> indicators = new(2);
			if (Describe("AGENT", "Agent forwarding", _forwarding.Agent) is { } agent)
			{
				indicators.Add(agent);
			}

			if (Describe("X11", "X11 forwarding", _forwarding.X11) is { } x11)
			{
				indicators.Add(x11);
			}

			return indicators;
		}
	}

	internal static SessionIndicator? Describe(string label, string name, SshForwardingLine line)
	{
		ArgumentNullException.ThrowIfNull(line);
		return line.State switch
		{
			SshForwardingState.Requested when line.OpenChannels > 0 => new SessionIndicator(
				label,
				SessionIndicatorState.Active,
				string.Create(
					CultureInfo.CurrentCulture,
					$"{name} to {line.Target}: {line.OpenChannels} {(line.OpenChannels == 1 ? "connection" : "connections")} from the server open now.")),
			SshForwardingState.Requested => new SessionIndicator(
				label,
				SessionIndicatorState.On,
				line.Problem is { Length: > 0 } problem
					? $"{name} to {line.Target} is on. The last connection failed: {problem}"
					: $"{name} to {line.Target} is on. Programs on the server can use it."),
			SshForwardingState.Unavailable => new SessionIndicator(
				label,
				SessionIndicatorState.Problem,
				$"{name} was asked for but is not working: {line.Problem ?? "the server did not set it up."}"),
			_ => null,
		};
	}
}
