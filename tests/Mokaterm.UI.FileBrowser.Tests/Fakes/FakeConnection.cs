using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>One live connection of a <see cref="FakeTab"/>, offering its file feature.</summary>
internal sealed class FakeConnection(FakeFileFeature feature) : IProtocolSession
{
	public Task Completion => Task.CompletedTask;

	public TFeature? GetFeature<TFeature>()
		where TFeature : class => feature as TFeature;

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
