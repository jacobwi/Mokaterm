using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

// A component injects services a render never calls; one that is called after all fails the test instead of passing quietly.
[SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "DispatchProxy derives the proxy from it at run time.")]
internal class UnusedService : DispatchProxy
{
	public static T Of<T>()
		where T : class => Create<T, UnusedService>();

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
		throw new InvalidOperationException($"{targetMethod?.Name} was called, which this render does not expect.");
}
