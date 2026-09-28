using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Ssh;

public static class SshMokatermBuilderExtensions
{
	/// <summary>Adds the ssh and sftp protocols, their editors and settings page, and the private key inspector.</summary>
	public static IMokatermBuilder AddSsh(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new SshModule());
	}
}
