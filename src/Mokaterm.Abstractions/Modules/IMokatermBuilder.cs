using Microsoft.Extensions.DependencyInjection;

namespace Mokaterm.Abstractions.Modules;

/// <summary>
/// Returned by <c>AddMokaterm()</c> so modules can hang their own <c>AddXxx()</c> extensions off it.
/// </summary>
public interface IMokatermBuilder
{
	IServiceCollection Services { get; }

	/// <summary>Adds a module once. A second module with the same id is ignored.</summary>
	IMokatermBuilder AddModule(IMokatermModule module);
}
