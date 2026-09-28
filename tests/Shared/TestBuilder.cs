using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Tests.Shared;

/// <summary>A module builder over a bare service collection, so a module's own registrations are all there is.</summary>
internal sealed class TestBuilder : IMokatermBuilder
{
	private readonly HashSet<string> _moduleIds = new(StringComparer.Ordinal);

	public IServiceCollection Services { get; } = new ServiceCollection();

	public IMokatermBuilder AddModule(IMokatermModule module)
	{
		if (_moduleIds.Add(module.Info.Id))
		{
			module.ConfigureServices(Services);
		}

		return this;
	}
}
