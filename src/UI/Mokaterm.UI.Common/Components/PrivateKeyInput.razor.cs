using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Paste or import a private key with its passphrase, validated as you type when a module registered an
/// <see cref="IPrivateKeyInspector"/>. Shared by the connection editor and the keychain.
/// </summary>
public partial class PrivateKeyInput : ComponentBase
{
	// Private keys are a few kilobytes; anything far larger is the wrong file, and it is refused rather than cut, since
	// half a key parses as no key at all.
	private const int MaxKeyFileBytes = 64 * 1024;

	private PrivateKeyInspection? _inspection;
	private string? _importError;

	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	[Inject]
	private ILogger<PrivateKeyInput> Logger { get; set; } = default!;

	[Parameter]
	public string? PrivateKey { get; set; }

	[Parameter]
	public EventCallback<string?> PrivateKeyChanged { get; set; }

	[Parameter]
	public string? Passphrase { get; set; }

	[Parameter]
	public EventCallback<string?> PassphraseChanged { get; set; }

	/// <summary>True when editing a credential that already has a key stored, so an empty field means "keep it".</summary>
	[Parameter]
	public bool HasSavedKey { get; set; }

	[Parameter]
	public bool Disabled { get; set; }

	/// <summary>Raised with the latest validation result, or null when the field is empty or no inspector exists.</summary>
	[Parameter]
	public EventCallback<PrivateKeyInspection?> InspectionChanged { get; set; }

	private IPrivateKeyInspector? Inspector => Services.GetService<IPrivateKeyInspector>();

	private ILocalFileAccess? FileAccess => Services.GetService<ILocalFileAccess>();

	protected override void OnParametersSet() => _inspection = Inspect(PrivateKey, Passphrase);

	private async Task OnPrivateKeyChangedAsync(string? value)
	{
		PrivateKey = value;
		_importError = null;
		await PrivateKeyChanged.InvokeAsync(value);
		await PublishInspectionAsync();
	}

	private async Task OnPassphraseChangedAsync(string? value)
	{
		Passphrase = value;
		await PassphraseChanged.InvokeAsync(value);
		await PublishInspectionAsync();
	}

	private async Task ImportAsync()
	{
		if (FileAccess is not { } fileAccess)
		{
			return;
		}

		_importError = null;
		PickedFile<string> picked;
		try
		{
			picked = await LocalFilePick.ReadTextAsync(fileAccess, MaxKeyFileBytes, "a private key");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// A file that is locked or gone, or a browser upload that broke off: the editor stays usable either way.
			Logger.LogWarning(ex, "Reading a private key file failed");
			_importError = "The key file could not be read.";
			return;
		}

		if (!picked.WasRead)
		{
			_importError = picked.Error;
			return;
		}

		await OnPrivateKeyChangedAsync(picked.Content.Trim());
	}

	private async Task PublishInspectionAsync()
	{
		_inspection = Inspect(PrivateKey, Passphrase);
		await InspectionChanged.InvokeAsync(_inspection);
	}

	private PrivateKeyInspection? Inspect(string? privateKey, string? passphrase) =>
		string.IsNullOrWhiteSpace(privateKey) || Inspector is not { } inspector
			? null
			: inspector.Inspect(privateKey, string.IsNullOrEmpty(passphrase) ? null : passphrase);

	private static string StatusText(PrivateKeyInspection inspection) => inspection.Status switch
	{
		PrivateKeyStatus.Valid => inspection.Algorithm ?? "Valid key",
		PrivateKeyStatus.PassphraseRequired => "Passphrase required",
		PrivateKeyStatus.WrongPassphrase => "Wrong passphrase",
		PrivateKeyStatus.Unsupported => "Unsupported key type",
		_ => "Not a valid private key",
	};
}
