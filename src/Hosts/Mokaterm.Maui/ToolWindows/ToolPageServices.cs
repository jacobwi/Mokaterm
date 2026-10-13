using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// How a tool window's page borrows the services of the window that opened it. A BlazorWebView normally starts a
/// scope of its own, which would mean a second, locked vault and a session manager with no sessions. Instead each tool
/// window gets a container built from the app's own registrations in which every singleton and every scoped service
/// that holds the window's state is the opening window's instance, while what belongs to one page is created fresh:
/// Blazor's JS runtime and navigation (a second page attached to the first page's runtime would take it over),
/// Moka.Red's menus, dialogs and toasts, anything that calls into the page, and the services the shell lists in
/// <c>MokatermUiServiceCollectionExtensions.PageServices</c>.
/// <para>
/// The borrowed instances are registered as singletons of that container, which is never disposed: a scope of it
/// disposes only what it created, so closing a tool window can never dispose the vault it borrowed. The container
/// itself goes when the window does.
/// </para>
/// </summary>
internal sealed class ToolPageServices
{
	// Services registered under these assemblies' types serve one page: Blazor's own, the JS runtime, MAUI's per-window
	// services and every Moka.Red service, which all render into the page they were created for.
	private static readonly string[] PageAssemblies =
		["Microsoft.AspNetCore.Components", "Microsoft.JSInterop", "Microsoft.Maui", "Moka.Red"];

	private readonly ServiceDescriptor[] _descriptors;
	private readonly HashSet<Type> _perPage;

	/// <param name="descriptors">Every registration of the app, as the host built it.</param>
	/// <param name="declaredPageServices">
	/// Scoped services that belong to the page although nothing about their type says so, such as the prompt queue. They
	/// do not make the services that take them per page as well: the session manager takes the prompt queue to ask for
	/// passwords, and it must stay the window's.
	/// </param>
	public ToolPageServices(IEnumerable<ServiceDescriptor> descriptors, IEnumerable<Type> declaredPageServices)
	{
		ArgumentNullException.ThrowIfNull(descriptors);
		ArgumentNullException.ThrowIfNull(declaredPageServices);
		_descriptors = [.. descriptors];
		_perPage = Classify(_descriptors, [.. declaredPageServices]);
	}

	/// <summary>Whether a tool window gets its own <paramref name="serviceType"/> rather than the opening window's.</summary>
	public bool IsPerPage(Type serviceType) => _perPage.Contains(serviceType);

	/// <summary>
	/// The first of <paramref name="mustBeShared"/> that a tool window would get a fresh copy of, or null when it would
	/// borrow them all. The host refuses to open tool windows while this names anything, rather than show a window with
	/// a second vault.
	/// </summary>
	public Type? FirstNotShared(IEnumerable<Type> mustBeShared) => mustBeShared.FirstOrDefault(IsPerPage);

	/// <summary>
	/// The services of one tool window over <paramref name="owner"/>, the opening window's scope. Never dispose the
	/// result; dispose the scopes made from it.
	/// </summary>
	/// <param name="owner">The opening window's scope.</param>
	/// <param name="own">
	/// Services that belong to the tool window itself and replace whatever the app registered for them, such as the
	/// chrome bound to its own native window: borrowing the main window's would move the main window's drag regions.
	/// </param>
	public IServiceProvider Borrow(IServiceProvider owner, IReadOnlyDictionary<Type, object>? own = null)
	{
		ArgumentNullException.ThrowIfNull(owner);
		own ??= new Dictionary<Type, object>();
		ServiceDescriptor[] descriptors = [.. _descriptors.Where(descriptor => descriptor.IsKeyedService || !own.ContainsKey(descriptor.ServiceType))];
		Dictionary<(Type, object?), int> counts = descriptors
			.GroupBy(descriptor => (descriptor.ServiceType, descriptor.ServiceKey))
			.ToDictionary(group => group.Key, group => group.Count());
		Dictionary<(Type, object?), int> seen = [];
		IServiceCollection services = new ServiceCollection();
		foreach (ServiceDescriptor descriptor in descriptors)
		{
			(Type, object?) slot = (descriptor.ServiceType, descriptor.ServiceKey);
			int index = seen.GetValueOrDefault(slot);
			seen[slot] = index + 1;
			services.Add(Rewrite(descriptor, owner, index, counts[slot]));
		}

		// Instances the container did not create are never disposed by it; the window that made them owns them.
		foreach ((Type type, object instance) in own)
		{
			services.AddSingleton(type, instance);
		}

		return services.BuildServiceProvider();
	}

	private static HashSet<Type> Classify(ServiceDescriptor[] descriptors, Type[] declared)
	{
		ServiceDescriptor[] scoped =
			[.. descriptors.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped && !descriptor.ServiceType.IsGenericTypeDefinition)];

		// What makes a service the page's: being one of the page's own, or taking one in its constructor. Followed to
		// the end, so a service built over a JS wrapper is the page's too.
		HashSet<Type> spreading = [typeof(IJSRuntime), typeof(NavigationManager)];
		foreach (ServiceDescriptor descriptor in scoped.Where(descriptor => FromPageAssembly(descriptor.ServiceType)))
		{
			_ = spreading.Add(descriptor.ServiceType);
		}

		bool grew = true;
		while (grew)
		{
			grew = false;
			foreach (ServiceDescriptor descriptor in scoped)
			{
				if (!spreading.Contains(descriptor.ServiceType)
					&& ImplementationOf(descriptor) is { } implementation
					&& TakesAny(implementation, spreading))
				{
					grew |= spreading.Add(descriptor.ServiceType);
				}
			}
		}

		HashSet<Type> perPage = [.. scoped.Select(descriptor => descriptor.ServiceType).Where(spreading.Contains)];
		perPage.UnionWith(declared);
		return perPage;
	}

	private static bool FromPageAssembly(Type type)
	{
		string name = type.Assembly.GetName().Name ?? "";
		return PageAssemblies.Any(prefix => name.Equals(prefix, StringComparison.Ordinal) || name.StartsWith(prefix + ".", StringComparison.Ordinal));
	}

	private static Type? ImplementationOf(ServiceDescriptor descriptor) =>
		descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

	// Every public constructor, not only the one the container would pick: wrongly calling a service the page's costs a
	// fresh copy in a tool window, wrongly calling it the window's could send a tool window's calls to another page.
	private static bool TakesAny(Type implementation, HashSet<Type> types) =>
		implementation.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
			.SelectMany(constructor => constructor.GetParameters())
			.Select(parameter => ElementType(parameter.ParameterType))
			.Any(types.Contains);

	private static Type ElementType(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type.GetGenericArguments()[0] : type;

	private static bool HasInstance(ServiceDescriptor descriptor) =>
		(descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance) is not null;

	private ServiceDescriptor Rewrite(ServiceDescriptor descriptor, IServiceProvider owner, int index, int count)
	{
		// Open generics cannot be forwarded one closed type at a time; they are the framework's loggers and options,
		// which hold nothing a window owns, so the tool window simply makes its own.
		if (descriptor.ServiceType.IsGenericTypeDefinition)
		{
			return descriptor;
		}

		return descriptor.Lifetime switch
		{
			ServiceLifetime.Singleton when !HasInstance(descriptor) => Forward(descriptor, owner, index, count),
			ServiceLifetime.Scoped when !_perPage.Contains(descriptor.ServiceType) => Forward(descriptor, owner, index, count),
			_ => descriptor,
		};
	}

	private static ServiceDescriptor Forward(ServiceDescriptor descriptor, IServiceProvider owner, int index, int count)
	{
		Type type = descriptor.ServiceType;
		if (descriptor.IsKeyedService)
		{
			object? key = descriptor.ServiceKey;
			return ServiceDescriptor.KeyedSingleton(type, key, (_, _) => ResolveKeyed(owner, type, key, index, count));
		}

		return ServiceDescriptor.Singleton(type, _ => Resolve(owner, type, index, count));
	}

	// The same registration of the owner, by position, so a service registered several times (protocol providers,
	// settings pages) keeps every instance and a single lookup still finds the last one.
	private static object Resolve(IServiceProvider owner, Type type, int index, int count) =>
		count == 1
			? owner.GetRequiredService(type)
			: owner.GetServices(type).ElementAt(index) ?? throw new InvalidOperationException($"{type.Name} resolved to nothing.");

	private static object ResolveKeyed(IServiceProvider owner, Type type, object? key, int index, int count) =>
		count == 1
			? owner.GetRequiredKeyedService(type, key)
			: owner.GetKeyedServices(type, key).ElementAt(index) ?? throw new InvalidOperationException($"{type.Name} resolved to nothing.");
}
