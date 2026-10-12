namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// Names the module a plugin assembly adds. The loader reads this instead of scanning the assembly's types, so a plugin
/// says exactly what runs and nothing is started by accident.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MokatermPluginAttribute : Attribute
{
	public MokatermPluginAttribute(Type moduleType)
	{
		ArgumentNullException.ThrowIfNull(moduleType);
		ModuleType = moduleType;
	}

	/// <summary>An <see cref="Modules.IMokatermModule"/> with a public constructor that takes nothing.</summary>
	public Type ModuleType { get; }
}
