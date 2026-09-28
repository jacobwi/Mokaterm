using System.Reflection;

namespace Mokaterm.Abstractions.Platform;

/// <summary>
/// The version people read for an assembly: <see cref="IAppEnvironment.AppVersion"/> for hosts and
/// <see cref="Modules.ModuleInfo.Version"/> for modules.
/// </summary>
public static class DisplayVersion
{
	/// <summary>
	/// The informational version without the <c>+commit</c> build metadata the SDK appends, or the assembly version when
	/// there is no informational one.
	/// </summary>
	public static string Of(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (informational is not null)
		{
			int metadata = informational.IndexOf('+', StringComparison.Ordinal);
			string version = (metadata >= 0 ? informational[..metadata] : informational).Trim();
			if (version.Length > 0)
			{
				return version;
			}
		}

		Version? assemblyVersion = assembly.GetName().Version;
		return assemblyVersion is null
			? "0.0.0"
			: new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0)).ToString();
	}
}
