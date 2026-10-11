namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Marks a setting whose value only means something on the machine that set it: a folder, a feed path, a program that
/// is installed here. A settings export leaves it out, and an import keeps this machine's own value, so moving a setup
/// to another computer never points it at a folder that is not there. Only read on sections the backup knows the type
/// of, which is every section with a settings page (see <see cref="SettingsSectionType"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MachineSpecificAttribute : Attribute;
