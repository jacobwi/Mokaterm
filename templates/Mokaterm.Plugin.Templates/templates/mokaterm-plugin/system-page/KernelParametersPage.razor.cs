using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.ToolPages;

namespace MokatermPlugin1;

/// <summary>
/// A page in the System view of SSH sessions: the kernel parameters named in the plugin's settings, read from
/// <c>/proc/sys</c> in one script and again on the settings' interval while the view shows. Scripts run through the
/// view, so they get its time limit, its exec channel budget and root mode like the built-in pages.
/// </summary>
public sealed partial class KernelParametersPage : SystemPageBase
{
	private IReadOnlyList<string> _names = [];
	private IReadOnlyList<KernelParameter> _parameters = [];
	private List<KernelParameter> _shown = [];
	private string _filter = "";

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	// A few parameters are root's alone, so turning root mode on reads the page again.
	protected override bool ReadsMoreAsRoot => true;

	private string Summary => _names.Count == 0
		? "none chosen"
		: string.Create(CultureInfo.CurrentCulture, $"{_parameters.Count(parameter => parameter.Value is not null)} of {_names.Count} read");

	// Read once when the page opens: changed settings apply the next time it is shown.
	protected override void OnInitialized()
	{
		PluginSettings settings = SettingsService.Get<PluginSettings>().Clamped();
		_names = PluginSettings.Split(settings.Parameters);
		StartPolling(TimeSpan.FromSeconds(settings.RefreshSeconds), PollAsync);
	}

	protected override async Task LoadAsync(CancellationToken cancellationToken)
	{
		if (_names.Count == 0)
		{
			_parameters = [];
			ApplyFilter();
			return;
		}

		await ReadParametersAsync(cancellationToken);
	}

	/// <exception cref="SystemScriptException">The script failed, or did not run to its end.</exception>
	private async Task ReadParametersAsync(CancellationToken cancellationToken)
	{
		bool asRoot = ReadAsRoot;
		SystemScriptResult result = await ReadAsync(KernelParameterCommands.Read(_names, asRoot), cancellationToken);
		if (!result.Succeeded)
		{
			throw new SystemScriptException($"The parameters could not be read: {result.Problem}");
		}

		_parameters = KernelParameterCommands.Parse(result.Output, _names, asRoot);
		ApplyFilter();
	}

	private async Task PollAsync()
	{
		if (_names.Count == 0 || LoadError is not null)
		{
			return;
		}

		try
		{
			await ReadParametersAsync(Lifetime);
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
			? [.. _parameters]
			: [.. _parameters.Where(parameter =>
				parameter.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
				|| (parameter.Value?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))];
	}
}
