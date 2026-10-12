using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Common.Contributions;

public static partial class UiContributionServiceCollectionExtensions
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
		services.AddSingleton(new SettingsPageName(descriptor.Id));
		services.TryAddSingleton<IUiContributions, UiContributions>();
		if (SectionTypeOf(descriptor.Component) is { } section)
		{
			services.AddSingleton(section);
		}

		return services;
	}

	/// <summary>
	/// Adds a page to the System view's side menu, among the view's own pages by its order. A later registration with the
	/// same id replaces an earlier one. Everything is checked here, so a mistake fails at startup.
	/// </summary>
	public static IServiceCollection AddSystemPage(this IServiceCollection services, SystemPageDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsurePage(descriptor.Id, descriptor.Title, descriptor.Component);
		EnsureDerivesFrom<SystemPageBase>(descriptor.Component, nameof(descriptor.Component));
		ArgumentNullException.ThrowIfNull(descriptor.Commands);
		if (descriptor.Commands.FirstOrDefault(command => !ServerFacts.IsCommandName(command)) is { } command)
		{
			throw new ArgumentException($"\"{command}\" is not a program name the System view can look for.", nameof(descriptor));
		}

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	/// <summary>
	/// Adds a page to the Docker manager's side menu, among the manager's own pages by its order. A later registration
	/// with the same id replaces an earlier one.
	/// </summary>
	public static IServiceCollection AddDockerPage(this IServiceCollection services, DockerPageDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsurePage(descriptor.Id, descriptor.Title, descriptor.Component);
		EnsureDerivesFrom<DockerPageBase>(descriptor.Component, nameof(descriptor.Component));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	/// <summary>
	/// Adds a tool that needs no session to the top bar's Tools menu and the command palette. A later registration with
	/// the same id replaces an earlier one.
	/// </summary>
	public static IServiceCollection AddAppTool(this IServiceCollection services, AppToolDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		EnsurePage(descriptor.Id, descriptor.Title, descriptor.Component);
		EnsureDerivesFrom<AppToolBase>(descriptor.Component, nameof(descriptor.Component));

		services.AddSingleton(descriptor);
		services.TryAddSingleton<IUiContributions, UiContributions>();
		return services;
	}

	/// <summary>Whether <paramref name="id"/> can name a tool page or an app tool.</summary>
	public static bool IsToolId(string? id) => id is not null && ToolIdPattern().IsMatch(id);

	private static void EnsurePage(string id, string title, Type component)
	{
		if (!IsToolId(id))
		{
			throw new ArgumentException($"\"{id}\" is not an id: use lower case letters, digits and dashes, starting with a letter or a digit.", nameof(id));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		ArgumentNullException.ThrowIfNull(component);
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

	// \z rather than $: an id ends palette command ids and saved shortcut names.
	[GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ToolIdPattern();
}
