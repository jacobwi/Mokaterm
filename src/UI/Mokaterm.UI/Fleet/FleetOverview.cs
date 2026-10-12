using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Connections;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// The fleet overview for one window: the survey behind it and the logins it lists. It belongs to the window rather
/// than the dialog, so closing the dialog keeps what was read for the next opening, and a read still going finishes.
/// Nothing is written anywhere: what a machine runs is for this window to see while it is unlocked, so locking the
/// vault drops the survey with every result in it.
/// </summary>
internal sealed class FleetOverview : IDisposable
{
	private readonly IHostSurveyRunner _runner;
	private readonly CatalogState _catalog;
	private readonly IProtocolRegistry _protocols;
	private readonly ISettingsService _settings;
	private readonly IVault _vault;
	private readonly ILogger<FleetOverview> _logger;
	private readonly SemaphoreSlim _starting = new(1, 1);
	private IHostSurvey? _survey;
	private int _disposed;

	public FleetOverview(
		IHostSurveyRunner runner,
		CatalogState catalog,
		IProtocolRegistry protocols,
		ISettingsService settings,
		IVault vault,
		ILogger<FleetOverview> logger)
	{
		_runner = runner;
		_catalog = catalog;
		_protocols = protocols;
		_settings = settings;
		_vault = vault;
		_logger = logger;
		_vault.StatusChanged += OnVaultStatusChanged;
	}

	/// <summary>Raised after any host changed, a survey started or the results were dropped. Handlers may run on any thread.</summary>
	public event Action? Changed;

	/// <summary>True once this window read anything, until the vault locks.</summary>
	public bool HasSurvey => Volatile.Read(ref _survey) is not null;

	public bool IsRunning => Volatile.Read(ref _survey)?.IsRunning == true;

	/// <summary>
	/// Every saved login whose protocol can report on its machine, in the tree's order (machines by name, then their
	/// logins), each with what the window last read from it.
	/// </summary>
	public IReadOnlyList<FleetRow> Rows()
	{
		ConnectionCatalog catalog = _catalog.Catalog;
		Dictionary<Guid, HostSurveyResult> results = [];
		foreach (HostSurveyResult result in Volatile.Read(ref _survey)?.Hosts ?? [])
		{
			results[result.ConnectionId] = result;
		}

		List<FleetRow> rows = [];
		foreach (HostProfile host in catalog.Hosts.OrderBy(host => host.DisplayName, StringComparer.CurrentCultureIgnoreCase))
		{
			foreach (ConnectionProfile login in catalog.ConnectionsOf(host.Id))
			{
				if (HostSurveyEligibility.WhyNot(_protocols.Find(login.ProtocolId)?.Descriptor) is null)
				{
					rows.Add(new FleetRow(host, login, results.GetValueOrDefault(login.Id)));
				}
			}
		}

		return rows;
	}

	/// <summary>
	/// Reads <paramref name="connectionIds"/> again, or every login the overview lists when null, skipping any whose
	/// connect would ask something; a login being read already is left alone. Returns how many were queued.
	/// </summary>
	public async Task<int> RefreshAsync(IReadOnlyCollection<Guid>? connectionIds = null)
	{
		await _catalog.EnsureLoadedAsync();
		IReadOnlyCollection<Guid> wanted = connectionIds ?? [.. Rows().Select(row => row.Login.Id)];
		if (wanted.Count == 0)
		{
			return 0;
		}

		// One survey per window: two quick clicks must not start two.
		await _starting.WaitAsync();
		try
		{
			if (Volatile.Read(ref _disposed) != 0 || _vault.Status != VaultStatus.Unlocked)
			{
				return 0;
			}

			if (Volatile.Read(ref _survey) is { } survey)
			{
				return await survey.RefreshAsync(wanted);
			}

			Guid[] connections = [.. wanted.Distinct()];
			IHostSurvey started = await _runner.StartAsync(new HostSurveyRequest { ConnectionIds = connections, Options = Options() });
			if (Volatile.Read(ref _disposed) != 0 || _vault.Status != VaultStatus.Unlocked)
			{
				_ = started.DisposeAsync().AsTask();
				return 0;
			}

			started.Changed += OnSurveyChanged;
			Volatile.Write(ref _survey, started);
			Raise();
			return connections.Length;
		}
		finally
		{
			_starting.Release();
		}
	}

	/// <summary>Reads the skipped logins again, this time asking whatever their connect needs, one prompt at a time.</summary>
	public Task<int> RunSkippedAskingAsync() => Volatile.Read(ref _survey)?.RetrySkippedAsync() ?? Task.FromResult(0);

	public Task<int> RetryFailedAsync() => Volatile.Read(ref _survey)?.RetryFailedAsync() ?? Task.FromResult(0);

	/// <summary>Stops every read still going. What was read stays.</summary>
	public void Stop() => Volatile.Read(ref _survey)?.CancelAll();

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_vault.StatusChanged -= OnVaultStatusChanged;
		Release();
	}

	/// <summary>
	/// The command run's limits, except that a survey always skips logins that would ask on its first pass: opening an
	/// overview of forty machines must not turn into forty prompts. "Run skipped, asking" is how the rest get read.
	/// </summary>
	private CommandRunSettings Options() => _settings.Get<CommandRunSettings>().Clamped() with { SkipHostsThatAsk = true };

	private void OnVaultStatusChanged(VaultStatus status)
	{
		if (status != VaultStatus.Unlocked)
		{
			Release();
		}
	}

	/// <summary>Drops the survey and with it every result; its connections close in the background.</summary>
	private void Release()
	{
		if (Interlocked.Exchange(ref _survey, null) is not { } survey)
		{
			return;
		}

		survey.Changed -= OnSurveyChanged;
		_ = DisposeQuietlyAsync(survey);
		Raise();
	}

	private async Task DisposeQuietlyAsync(IHostSurvey survey)
	{
		try
		{
			await survey.DisposeAsync();
		}
		catch (Exception ex)
		{
			_logger.LogWarning("Closing the fleet overview's survey failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	private void OnSurveyChanged() => Raise();

	private void Raise()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.LogWarning("A fleet overview handler failed: {Failure}", LogSafe.Describe(ex));
		}
	}
}
