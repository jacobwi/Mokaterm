using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Connection;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Ftp.Components;

/// <summary>The FTP part of the connection editor: encryption, data connection mode, character set and initial folder.</summary>
public partial class FtpOptionsEditor : ConnectionOptionsEditorBase<FtpConnectionOptions>
{
	private string EncryptionHelp => Current.Encryption switch
	{
		FtpEncryption.Explicit => "Starts in plain text and switches to TLS with AUTH TLS, usually on port 21.",
		FtpEncryption.Implicit => "TLS from the first byte. Implicit FTPS usually uses port 990, which is used when the port is left empty.",
		_ => "Plain FTP: the password and files travel unencrypted. Choose explicit TLS when the server supports it.",
	};

	private string DataConnectionHelp => Current.DataConnection switch
	{
		FtpDataConnectionMode.ExtendedPassive => "EPSV: like passive without an address in the reply. Suits IPv6 and servers behind NAT.",
		FtpDataConnectionMode.Active => "PORT: the server connects back to the machine running Mokaterm, which needs an open port.",
		_ => "PASV: Mokaterm opens each data connection, always to the server it is connected to. Works through most firewalls.",
	};

	protected override FtpConnectionOptions From(ProtocolOptions options) => FtpConnectionOptions.From(options);

	protected override ProtocolOptions ApplyTo(FtpConnectionOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>Implicit TLS answers on 990, so the port field shows that rather than the protocol's 21.</summary>
	protected override void OnCurrentChanged() =>
		ReportDefaultPort(Current.Encryption == FtpEncryption.Implicit ? FtpConnectionOptions.ImplicitTlsPort : null);

	private Task OnEncryptionChangedAsync(string value) =>
		Enum.TryParse(value, out FtpEncryption encryption) && Enum.IsDefined(encryption)
			? ChangeAsync(Current with { Encryption = encryption })
			: Task.CompletedTask;

	private Task OnDataConnectionChangedAsync(string value) =>
		Enum.TryParse(value, out FtpDataConnectionMode mode) && Enum.IsDefined(mode)
			? ChangeAsync(Current with { DataConnection = mode })
			: Task.CompletedTask;

	private Task OnEncodingChangedAsync(string value) => ChangeAsync(Current with { EncodingName = value });

	private Task OnInitialDirectoryChangedAsync(string? value) => ChangeAsync(Current with { InitialDirectory = value ?? "" });

	private Task OnProxyChangedAsync(ProxyOptions proxy) => ChangeAsync(Current with { Proxy = proxy });
}
