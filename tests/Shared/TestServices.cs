using Microsoft.Extensions.DependencyInjection;

namespace Mokaterm.Tests.Shared;

internal static class TestServices
{
	/// <summary>The descriptor instances registered for <typeparamref name="T"/>, which is how UI contributions arrive.</summary>
	public static IEnumerable<T> Registered<T>(this IServiceCollection services) =>
		services.Where(descriptor => descriptor.ServiceType == typeof(T)).Select(descriptor => descriptor.ImplementationInstance).OfType<T>();
}
