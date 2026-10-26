using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

internal sealed class FeatureSession(params object[] features) : IProtocolSession
{
	public Task Completion { get; } = new TaskCompletionSource().Task;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class => features.OfType<TFeature>().FirstOrDefault();

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
