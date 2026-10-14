using Mokaterm.Abstractions.Security;

namespace Mokaterm.RdpProbe;

/// <summary>
/// Shows the server's certificate and asks whether to go on, since the login goes out right after. Nothing is
/// remembered: the probe has no known hosts.
/// </summary>
internal sealed class ConsoleCertificateCheck : IHostIdentityVerifier
{
	private readonly ProbeOutput _output;
	private readonly bool _acceptWithoutAsking;

	public ConsoleCertificateCheck(ProbeOutput output, bool acceptWithoutAsking)
	{
		_output = output;
		_acceptWithoutAsking = acceptWithoutAsking;
	}

	public ValueTask<bool> VerifyAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(identity);
		_output.Detail($"Certificate: {identity.Subject}, issued by {identity.Issuer}, {identity.Algorithm}");
		_output.Detail($"SHA-256: {identity.Fingerprint}");
		_output.Detail(identity.ChainTrusted == true
			? "Windows trusts this certificate for this name."
			: "Windows does not trust this certificate for this name, which is normal for a PC's own Remote Desktop certificate.");
		if (_acceptWithoutAsking)
		{
			return ValueTask.FromResult(true);
		}

		Console.Write("             Send the login to this server? [y/N] ");
		string? answer = Console.ReadLine();
		return ValueTask.FromResult(string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase));
	}
}
