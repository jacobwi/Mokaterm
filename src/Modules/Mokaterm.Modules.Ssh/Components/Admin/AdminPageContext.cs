using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>What every page of the system view gets from the view around it.</summary>
internal sealed class AdminPageContext
{
	private string? _hint;

	public required SystemAdminFeature Feature { get; init; }

	public required SystemProfile Profile { get; init; }

	/// <summary>Shows the action's commands, waits for the user, then runs it. Null when the user cancelled.</summary>
	public required Func<AdminAction, AdminInput?, Task<AdminActionResult?>> Run { get; init; }

	/// <summary>Turns root mode on from inside a page, the way the header's button does.</summary>
	public required Func<Task> EnableRoot { get; init; }

	/// <summary>Opens another page, as an overview card does.</summary>
	public required Action<AdminPage> Navigate { get; init; }

	/// <summary>The address this session connected to, as the connection names it.</summary>
	public required string SessionAddress { get; init; }

	/// <summary>True while a change is under review or running; pages turn their buttons off meanwhile.</summary>
	public required Func<bool> IsBusy { get; init; }

	/// <summary>The account this session logged in as.</summary>
	public string LoginName => Profile.Facts.UserName;

	/// <summary>Changes that need root can run here, now or after a sudo prompt.</summary>
	public bool CanChangeAsRoot => Feature.CanActAsRoot || Feature.RootBlockedReason is null;

	/// <summary>Why changes that need root are not offered, or null.</summary>
	public string? RootChangeBlocked => CanChangeAsRoot ? null : Feature.RootBlockedReason;

	/// <summary>Opens <paramref name="page"/> with a filter already chosen, such as the failed services.</summary>
	public void NavigateWith(AdminPage page, string hint)
	{
		_hint = hint;
		Navigate(page);
	}

	/// <summary>The filter the page was opened with, once; later calls get null.</summary>
	public string? TakeHint()
	{
		string? hint = _hint;
		_hint = null;
		return hint;
	}
}
