using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Import;

/// <summary>
/// Writes every saved host and login to one file. The file is built when the dialog opens, so the counts on screen are
/// what the Save button writes, and the host decides where it lands: a picker on the desktop, a download on the web.
/// </summary>
public sealed partial class ExportDialog : ShellEditorBase<ExportEditorRequest>
{
	private const string MutedStyle = "color:var(--moka-color-on-surface-variant)";

	private ConnectionExport? _export;
	private string? _error;
	private bool _busy;

	[Inject]
	private IConnectionExporter Exporter { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private ILogger<ExportDialog> Logger { get; set; } = default!;

	private bool CanSaveFiles => Services.GetService<ILocalFileAccess>() is not null;

	protected override bool IsBusy => _busy;

	protected override bool SubmitOnEnter => true;

	protected override void Reset()
	{
		_export = null;
		_error = null;
	}

	protected override async Task LoadAsync(ExportEditorRequest request)
	{
		_busy = true;
		try
		{
			_export = await Exporter.ExportAsync();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Building the connections export failed.");
			_error = "The export could not be built.";
		}
		finally
		{
			_busy = false;
		}
	}

	private static string HostsText(ConnectionExport export) => export.HostsWithoutLogin switch
	{
		0 => "Every saved machine",
		1 => "One machine has no login yet and is left out",
		_ => string.Create(CultureInfo.CurrentCulture, $"{export.HostsWithoutLogin} machines have no login yet and are left out"),
	};

	private async Task SaveAsync()
	{
		if (_busy || _export is not { } export || Services.GetService<ILocalFileAccess>() is not { } files)
		{
			return;
		}

		_busy = true;
		_error = null;
		try
		{
			bool saved = await files.SaveFileAsync(
				export.FileName,
				export.Content.Length,
				async (stream, cancellationToken) => await stream.WriteAsync(export.Content, cancellationToken));

			if (saved)
			{
				Interaction.Notify(
					NoticeSeverity.Success,
					string.Create(CultureInfo.CurrentCulture, $"{export.Logins} logins written to {export.FileName}."),
					"Exported");

				// The file is on disk, so the dialog goes whether or not the busy flag has been cleared yet.
				CloseSaved();
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError(ex, "Saving the connections export failed.");
			_error = "The file could not be saved.";
		}
		finally
		{
			_busy = false;
		}
	}
}
