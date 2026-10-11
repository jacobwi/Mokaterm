using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

public static class UiContributionServiceCollectionExtensions
{
	// SectionKey is a static abstract member, which only a generic method can read from a type known at run time.
	private static readonly MethodInfo SectionTypeFactory =
		typeof(SettingsSectionType).GetMethod(nameof(SettingsSectionType.Of), BindingFlags.Public | BindingFlags.Static)!;

	/// <summary>Registers how a protocol looks in the shell. Validates component types eagerly so mistakes fail at startup.</summary>
	public static IServiceCollection AddProtocolUi(this IServiceCollection services, ProtocolUiDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsureDerivesFrom<ConnectionOptionsEditorBase>(descriptor.OptionsEditor, nameof(descriptor.OptionsEditor));
		EnsureDerivesFrom<SessionViewBase>(descriptor.SessionView, nameof(descriptor.SessionView));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	/// <summary>
	/// Adds a view to the session toolbar of one protocol. Registered next to <see cref="AddProtocolUi"/> by the
	/// module that owns the protocol, so the shell needs no knowledge of it.
	/// </summary>
	public static IServiceCollection AddSessionTool(this IServiceCollection services, SessionToolDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsureDerivesFrom<SessionViewBase>(descriptor.Component, nameof(descriptor.Component));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	/// <summary>
	/// Adds a page to Settings. A later registration with the same id replaces an earlier one. A page built on
	/// <see cref="SettingsSectionBase{TSettings}"/> also registers its section's <see cref="SettingsSectionType"/>, which is
	/// how the settings backup knows every section's type without a list of its own.
	/// </summary>
	public static IServiceCollection AddSettingsPage(this IServiceCollection services, SettingsPageDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsureDerivesFrom<IComponent>(descriptor.Component, nameof(descriptor.Component));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		if (SectionTypeOf(descriptor.Component) is { } section)
		{
			services.AddSingleton(section);
		}

		return services;
	}

	private static SettingsSectionType? SectionTypeOf(Type component)
	{
		for (Type? type = component; type is not null; type = type.BaseType)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SettingsSectionBase<>))
			{
				return (SettingsSectionType?)SectionTypeFactory.MakeGenericMethod(type.GetGenericArguments()).Invoke(null, null);
			}
		}

		return null;
	}

	private static void EnsureDerivesFrom<TBase>(Type? type, string parameterName)
	{
		if (type is not null && !typeof(TBase).IsAssignableFrom(type))
		{
			throw new ArgumentException($"{type.FullName} must derive from or implement {typeof(TBase).Name}.", parameterName);
		}
	}
}
