using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.Settings.Pages.About;

/// <summary>App version, platform, runtime, loaded modules and protocols, and credits.</summary>
public sealed partial class AboutPage : ComponentBase
{
	private static readonly IReadOnlyList<Credit> Credits =
	[
		new("Moka.Red", "Blazor components and theming. MIT license.", "https://github.com/jacobwi/Moka.Red"),
		new("SSH.NET", "SSH and SFTP. MIT license.", "https://github.com/sshnet/SSH.NET"),
		new("FluentFTP", "FTP and FTPS. MIT license.", "https://github.com/robinrodricks/FluentFTP"),
		new("xterm.js", "Terminal emulator. MIT license.", "https://github.com/xtermjs/xterm.js"),
	];

	private IReadOnlyList<ModuleInfo> _modules = [];

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	[Inject]
	private IProtocolRegistry ProtocolRegistry { get; set; } = default!;

	[Inject]
	private IEnumerable<ModuleInfo> ModuleInfos { get; set; } = default!;

	private bool IsDesktop => AppEnvironment.Kind == HostKind.Desktop;

	private string HostDescription => IsDesktop
		? "Runs on this device."
		: "Runs in the browser. Connections are made by the server.";

	private string RuntimeDescription
	{
		get
		{
			string system = $"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}";
			return IsDesktop ? system : $"Server: {system}";
		}
	}

	protected override void OnInitialized() =>
		_modules = [.. ModuleInfos.OrderBy(module => module.Name, StringComparer.CurrentCultureIgnoreCase)];

	private static string ProtocolDescription(ProtocolDescriptor protocol)
	{
		// A serial line has no port, so it gets its description alone rather than "Default port 0".
		string port = protocol.UsesPort
			? string.Create(CultureInfo.InvariantCulture, $"Default port {protocol.DefaultPort}.")
			: "";
		return string.IsNullOrWhiteSpace(protocol.Description)
			? port
			: string.IsNullOrEmpty(port) ? protocol.Description : $"{protocol.Description.TrimEnd('.')}. {port}";
	}

	private static IEnumerable<string> CapabilityNames(ProtocolDescriptor protocol)
	{
		foreach (ProtocolCapabilities capability in Enum.GetValues<ProtocolCapabilities>())
		{
			if (capability != ProtocolCapabilities.None && protocol.Has(capability))
			{
				yield return capability switch
				{
					ProtocolCapabilities.FileSystem => "File system",
					ProtocolCapabilities.RemoteDesktop => "Remote desktop",
					_ => capability.ToString(),
				};
			}
		}
	}

	private sealed record Credit(string Name, string Description, string Link);
}
