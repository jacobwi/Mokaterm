using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Plugins.Sample.Fail2ban;

/// <summary>
/// fail2ban's jails and the addresses each one bans, with a reviewed Unban as root. It reads every jail in one script,
/// as root when root mode allows it, and again every half minute while the view shows, since bans come and go.
/// </summary>
public sealed partial class Fail2banPage : SystemPageBase
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

	private Fail2banStatus _status = Fail2banStatus.Empty;
	private List<Fail2banJail> _shown = [];
	private string _filter = "";
	private string? _selected;
	private bool _needsRoot;

	/// <summary>fail2ban's socket answers root, so turning root mode on brings the jails.</summary>
	protected override bool ReadsMoreAsRoot => true;

	private string? Unavailable => Fail2banCommands.Unavailable(Context.Server);

	private List<Fail2banJail> Shown => _shown;

	private Fail2banJail? Selected => _selected is { } name ? _status.Jails.FirstOrDefault(jail => jail.Name == name) : null;

	private string Summary => Unavailable is not null
		? "not installed"
		: _needsRoot
			? "needs root mode"
			: string.Create(CultureInfo.CurrentCulture, $"{_status.Jails.Count} jails, {_status.BannedNow} addresses banned");

	protected override void OnInitialized() => StartPolling(PollInterval, PollAsync);

	protected override async Task LoadAsync(CancellationToken cancellationToken)
	{
		if (Unavailable is not null)
		{
			_status = Fail2banStatus.Empty;
			ApplyFilter();
			return;
		}

		await ReadStatusAsync(cancellationToken);
	}

	private static string Number(int? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "-";

	private static MokaColor DotColor(Fail2banJail jail) =>
		jail.Problem is not null ? MokaColor.Error : jail.CurrentlyBanned > 0 ? MokaColor.Warning : MokaColor.Success;

	/// <summary>Reads every jail. A failure other than fail2ban refusing the login is the frame's error.</summary>
	/// <exception cref="SystemScriptException">The script failed, or did not run to its end.</exception>
	private async Task ReadStatusAsync(CancellationToken cancellationToken)
	{
		SystemScriptResult result = await ReadAsync(Fail2banCommands.Status(ReadAsRoot), cancellationToken);
		_needsRoot = !ReadAsRoot && Fail2banCommands.NeedsRoot(result);
		if (!result.Succeeded && !_needsRoot)
		{
			throw new SystemScriptException($"fail2ban did not answer: {result.Problem}");
		}

		_status = result.Succeeded ? Fail2banParser.Parse(result.Output) : Fail2banStatus.Empty;
		ApplyFilter();
	}

	private async Task PollAsync()
	{
		if (Unavailable is not null || _needsRoot || LoadError is not null)
		{
			return;
		}

		try
		{
			await ReadStatusAsync(Lifetime);
		}
		catch (SystemScriptException)
		{
			// The next read the user asks for shows what went wrong; a poll keeps what is on screen.
			return;
		}
		catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
		{
			return;
		}

		StateHasChanged();
	}

	private void OnFilterChanged(string filter)
	{
		_filter = filter;
		ApplyFilter();
	}

	private void ApplyFilter()
	{
		string filter = _filter.Trim();
		_shown = filter.Length == 0
			? [.. _status.Jails]
			: [.. _status.Jails.Where(jail =>
				jail.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
				|| jail.BannedAddresses.Any(address => address.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
	}

	private void Select(Fail2banJail jail) => _selected = jail.Name == _selected ? null : jail.Name;

	private void OnRowKey(KeyboardEventArgs args, Fail2banJail jail)
	{
		if (args.Key is "Enter" or " ")
		{
			Select(jail);
		}
	}

	private void CloseDetail() => _selected = null;

	private Task EnableRootAsync() => Context.EnableRootAsync();

	/// <summary>Why this address cannot be unbanned from here, or null.</summary>
	private string? UnbanRefusal(string address) =>
		Context.RootChangeBlocked ?? Fail2banCommands.AddressProblem(address);

	private async Task UnbanAsync(Fail2banJail jail, string address)
	{
		if (UnbanRefusal(address) is not null || Fail2banCommands.JailProblem(jail.Name) is not null)
		{
			return;
		}

		await RunChangeAsync(Fail2banCommands.Unban(jail.Name, address));
	}
}
