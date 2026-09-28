using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Serial.Components;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Serial.Tests;

/// <summary>
/// The longest of the module settings pages, rendered for real. It is the one built from every shared field at once,
/// so it is what catches a row, a field or an enum control that only breaks when a renderer walks it.
/// </summary>
public sealed class SerialSettingsPageRenderTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_ShowsEverySectionAndWhatTheStoredLineIs()
	{
		string html = await RenderAsync(new SerialSettings
		{
			BaudRate = 9600,
			DataBits = 7,
			Parity = SerialParity.Even,
			EncodingName = "ibm437",
		});

		Assert.Contains("The line a new connection starts from", html, StringComparison.Ordinal);
		Assert.Contains("What a new connection sends", html, StringComparison.Ordinal);
		Assert.Contains("Every port", html, StringComparison.Ordinal);

		// The fields show what is stored, and the frame line reads it back the way a data sheet writes it.
		Assert.Contains("value=\"9600\"", html, StringComparison.Ordinal);
		Assert.Contains("value=\"ibm437\"", html, StringComparison.Ordinal);
		Assert.Contains("9600 7E1", html, StringComparison.Ordinal);

		// "Reset to defaults" belongs to the shared frame, so every page has it in the same place.
		Assert.Contains("Character set", html, StringComparison.Ordinal);
		Assert.Contains("Reset to defaults", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AFrameNoUartCanDo_IsShownCorrected()
	{
		// Eight data bits with one and a half stop bits does not exist, so the line reads back with one stop bit.
		string html = await RenderAsync(new SerialSettings { DataBits = 8, StopBits = SerialStopBits.OnePointFive });

		Assert.Contains("115200 8N1", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(SerialSettings settings)
	{
		FakeSettingsService stored = new();
		stored.Set(settings);

		return StaticRender.RenderAsync<SerialSettingsPage>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(stored);
				services.AddSingleton<IUserInteraction, DismissingInteraction>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);
	}
}
