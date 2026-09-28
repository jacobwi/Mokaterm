using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Common.Contributions;

public static class UiContributionServiceCollectionExtensions
{
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

	/// <summary>Adds a page to Settings. A later registration with the same id replaces an earlier one.</summary>
	public static IServiceCollection AddSettingsPage(this IServiceCollection services, SettingsPageDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsureDerivesFrom<IComponent>(descriptor.Component, nameof(descriptor.Component));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	private static void EnsureDerivesFrom<TBase>(Type? type, string parameterName)
	{
		if (type is not null && !typeof(TBase).IsAssignableFrom(type))
		{
			throw new ArgumentException($"{type.FullName} must derive from or implement {typeof(TBase).Name}.", parameterName);
		}
	}
}
