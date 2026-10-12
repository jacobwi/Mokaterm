using System.Globalization;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The "Check for updates" button of the images page and a compose project: it asks again even for images checked a
/// moment ago, because the user asked, and the answers stay in the session for every page to mark.
/// </summary>
public sealed partial class DockerUpdateCheck : DockerPageBase
{
	private bool _checking;
	private int _done;
	private int _total;

	/// <summary>The image references to ask about, as the page shows them.</summary>
	[Parameter, EditorRequired]
	public IReadOnlyList<string> References { get; set; } = [];

	/// <summary>Raised after a check, so the page draws its chips.</summary>
	[Parameter]
	public EventCallback OnChecked { get; set; }

	private string Text => _checking
		? string.Create(CultureInfo.CurrentCulture, $"Checking {_done}/{_total}")
		: "Check for updates";

	private string Title => Feature?.ImageUpdates.LastChecked is { } last
		? $"Asks each image's registry whether its tag points at a newer image; nothing is pulled. Last checked {DisplayFormat.Relative(last, Time.GetUtcNow())}."
		: "Asks each image's registry whether its tag points at a newer image; nothing is pulled.";

	private async Task CheckAsync()
	{
		if (_checking || Feature is not { } feature || Client is not { } client)
		{
			return;
		}

		_checking = true;
		_done = 0;
		_total = References.Count;
		IReadOnlyList<ImageUpdateResult> results;
		try
		{
			results = await feature.ImageUpdates.CheckAsync(client, References, force: true, Progress, Lifetime);
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			Interaction.Notify(NoticeSeverity.Error, $"Could not check for updates: {problem}");
			return;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_checking = false;
		}

		Interaction.Notify(results.Any(result => result.HasUpdate) ? NoticeSeverity.Info : NoticeSeverity.Success, Summary(results));
		await OnChecked.InvokeAsync();
	}

	private void Progress(int done, int total)
	{
		_done = done;
		_total = total;
		StateHasChanged();
	}

	private static string Summary(IReadOnlyList<ImageUpdateResult> results)
	{
		int newer = results.Count(result => result.HasUpdate);
		int known = results.Count(result => result.IsKnown);
		List<ImageUpdateResult> unknown = [.. results.Where(result => !result.IsKnown && result.State is not (ImageUpdateState.LocalOnly or ImageUpdateState.NoTag))];
		string found = newer switch
		{
			0 when known == 0 => "No image could be compared with a registry.",
			0 => "Every image a registry answered for is current.",
			1 => "One image has a newer one on its registry.",
			_ => string.Create(CultureInfo.CurrentCulture, $"{newer} images have newer ones on their registries."),
		};

		return unknown.Count switch
		{
			0 => found,
			1 => $"{found} {unknown[0].Reference} could not be checked: {unknown[0].Detail}",
			_ => string.Create(CultureInfo.CurrentCulture, $"{found} {unknown.Count} could not be checked. {unknown[0].Reference}: {unknown[0].Detail}"),
		};
	}
}
