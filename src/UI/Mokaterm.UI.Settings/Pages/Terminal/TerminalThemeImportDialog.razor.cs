using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Settings.Pages.Terminal;

/// <summary>Reads a color scheme from pasted text or a file and hands the theme back to be saved as a custom theme.</summary>
public sealed partial class TerminalThemeImportDialog : ComponentBase
{
	// Color schemes are a few kilobytes; a whole settings.json with many schemes is still far below this. Past it the
	// text is refused: a scheme cut in half either fails to parse or, worse, reads as a theme with half its colors.
	private const int MaxTextLength = 512 * 1024;

	private string _text = "";
	private string _name = "";
	private string? _fileName;
	private TerminalThemeImport? _import;

	[Parameter]
	public bool Open { get; set; }

	[Parameter]
	public EventCallback<bool> OpenChanged { get; set; }

	/// <summary>Font for the preview, so an imported scheme looks like it will in the terminal.</summary>
	[Parameter]
	public string FontFamily { get; set; } = "";

	[Parameter]
	public EventCallback<TerminalTheme> OnImport { get; set; }

	[Inject]
	private ITerminalThemeConverter Converter { get; set; } = default!;

	[Inject]
	private ILocalFileAccess LocalFiles { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private ILogger<TerminalThemeImportDialog> Logger { get; set; } = default!;

	private bool CanImport => _import?.Theme is not null && _name.Trim().Length > 0;

	protected override void OnParametersSet()
	{
		// The import state too, not only the text: a refused file leaves a message behind with nothing in the box, and
		// reopening the dialog would still be showing it.
		if (!Open && (_text.Length > 0 || _import is not null))
		{
			Reset();
		}
	}

	private static string FormatLabel(TerminalThemeFormat? format) => format switch
	{
		TerminalThemeFormat.WindowsTerminal => "Windows Terminal",
		TerminalThemeFormat.ITerm2 => "iTerm2",
		TerminalThemeFormat.Alacritty => "Alacritty",
		TerminalThemeFormat.Xresources => "X resources",
		TerminalThemeFormat.VsCode => "VS Code",
		_ => "Unknown format",
	};

	private TerminalTheme Named(TerminalTheme theme) => theme with { Name = _name.Trim().Length > 0 ? _name.Trim() : theme.Name };

	private Task OnTextChangedAsync(string value)
	{
		_fileName = null;
		return ReadAsync(value, fileName: null);
	}

	private void SetName(string value) => _name = value;

	private async Task PickFileAsync()
	{
		try
		{
			PickedFile<string> picked = await LocalFilePick.ReadTextAsync(LocalFiles, MaxTextLength, "a color scheme");
			if (picked.Error is { } refusal)
			{
				// The refusal goes where the scheme would have been, since the dialog is open and looking at it.
				_fileName = null;
				_text = "";
				_name = "";
				_import = TerminalThemeImport.Failure(refusal);
				return;
			}

			if (!picked.WasRead)
			{
				// The picker was closed; whatever was pasted stays.
				return;
			}

			_fileName = picked.Name;
			await ReadAsync(picked.Content, picked.Name);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// On the web host picking and reading go through the browser, so a JS error is as likely as an I/O one; only
			// the system's own I/O messages are worth showing as they are.
			Logger.LogWarning(ex, "Reading a color scheme file failed");
			string message = ex is IOException or UnauthorizedAccessException
				? ex.Message
				: "Pick the file again, or paste its text instead.";
			Interaction.Notify(NoticeSeverity.Error, message, "The file could not be read");
		}
	}

	private Task ReadAsync(string text, string? fileName)
	{
		_text = text;
		_import = TooLong(text) ?? (text.Trim().Length == 0 ? null : Converter.Import(text, fileName));
		_name = _import?.Theme?.Name ?? "";
		return Task.CompletedTask;
	}

	/// <summary>Pasted text is capped the same way a picked file is, and for the same reason.</summary>
	private static TerminalThemeImport? TooLong(string text) => text.Length <= MaxTextLength
		? null
		: TerminalThemeImport.Failure("That is too much text to be a color scheme.");

	private async Task ImportAsync()
	{
		if (_import?.Theme is not { } theme || !CanImport)
		{
			return;
		}

		await OnImport.InvokeAsync(Named(theme));
		await CloseAsync();
	}

	private Task CancelAsync() => CloseAsync();

	private async Task CloseAsync()
	{
		Reset();
		await OpenChanged.InvokeAsync(false);
	}

	private void Reset()
	{
		_text = "";
		_name = "";
		_fileName = null;
		_import = null;
	}
}
