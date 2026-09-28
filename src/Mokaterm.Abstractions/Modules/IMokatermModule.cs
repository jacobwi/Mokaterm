using Microsoft.Extensions.DependencyInjection;

namespace Mokaterm.Abstractions.Modules;

/// <summary>
/// A unit of functionality, usually one protocol family, that registers its services with the host.
/// </summary>
public interface IMokatermModule
{
	ModuleInfo Info { get; }

	void ConfigureServices(IServiceCollection services);
}
