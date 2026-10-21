using System.Globalization;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.RdpProbe;

/// <summary>
/// Prints what Windows says about signing in and works out where the session stopped. Windows sends a notice once a
/// user is signed in, or when it could not sign in with what it was given; a sign-in screen that is simply waiting
/// sends nothing, so silence is the only sign of one.
/// </summary>
internal sealed class SignInWatch
{
	private readonly ProbeOutput _output;
	private readonly Lock _gate = new();
	private readonly TaskCompletionSource<RdpLogonNotice> _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private RdpLogonNotice? _printed;

	public SignInWatch(ProbeOutput output) => _output = output;

	/// <summary>Takes a notice as the session reads it.</summary>
	public void Notice(RdpLogonNotice notice)
	{
		ArgumentNullException.ThrowIfNull(notice);
		lock (_gate)
		{
			// The session may have read one before the probe listened, and the probe asks for it as well.
			if (ReferenceEquals(notice, _printed))
			{
				return;
			}

			_printed = notice;
		}

		_output.Event($"Sign-in notice: {notice.Description} ({notice.Code})");
		if (notice.Kind != RdpLogonNoticeKind.Continuing)
		{
			_settled.TrySetResult(notice);
		}
	}

	/// <summary>Takes the notice the session read before anyone listened, if there was one.</summary>
	public void Catch(RdpLogonNotice? notice)
	{
		if (notice is not null)
		{
			Notice(notice);
		}
	}

	/// <summary>Waits for a notice that settles it, the session's end or <paramref name="wait"/>, and prints the outcome.</summary>
	public async Task WaitAsync(Task session, TimeSpan wait, CancellationToken cancellationToken)
	{
		Task finished = await Task.WhenAny(_settled.Task, session, Task.Delay(wait, cancellationToken));
		if (finished == _settled.Task)
		{
			RdpLogonNotice notice = await _settled.Task;
			_output.Event(notice.Kind switch
			{
				RdpLogonNoticeKind.SignedIn => "Signed in: Windows reported a signed-in user, so the session reached the desktop.",
				RdpLogonNoticeKind.SignInFailed => "Stopped at the sign-in screen: " + notice.Description,
				_ => "Stopped at a Windows dialog: " + notice.Description,
			});
			return;
		}

		_output.Event(finished == session
			? "The session ended before Windows said anything about signing in."
			: string.Create(
				CultureInfo.InvariantCulture,
				$"No sign-in notice within {wait.TotalSeconds:0} s. Windows sends one only once a user is signed in, so the session is most likely at its own sign-in screen, or the desktop is still starting."));
	}
}
