using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Core.Extensions;

/// <summary>Adds modules to a service collection, once per module id, and publishes their <see cref="ModuleInfo"/>.</summary>
public sealed class MokatermBuilder : IMokatermBuilder
{
	public MokatermBuilder(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		Services = services;
	}

	public IServiceCollection Services { get; }

	public IMokatermBuilder AddModule(IMokatermModule module)
	{
		ArgumentNullException.ThrowIfNull(module);
		ModuleInfo info = module.Info ?? throw new ArgumentException("The module has no info.", nameof(module));
		if (string.IsNullOrWhiteSpace(info.Id))
		{
			throw new ArgumentException("The module info needs an id.", nameof(module));
		}

		// Checks the collection rather than this builder, so a module added through an earlier AddMokaterm call counts too.
		bool registered = Services.Any(descriptor =>
			descriptor.ServiceType == typeof(ModuleInfo)
			&& !descriptor.IsKeyedService
			&& descriptor.ImplementationInstance is ModuleInfo existing
			&& string.Equals(existing.Id, info.Id, StringComparison.OrdinalIgnoreCase));
		if (registered)
		{
			return this;
		}

		module.ConfigureServices(Services);
		Services.AddSingleton(info);
		return this;
	}
}
