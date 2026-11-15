using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.Components;

/// <summary>
/// Event handlers that leave the component they belong to unrendered, from "Avoid rerendering after handling events
/// without state changes" in the Blazor rendering performance docs: the receiver is an <see cref="IHandleEvent"/> that
/// only runs the handler, so <see cref="EventCallbackFactory"/> hands it the event instead of the component.
/// </summary>
internal static class EventUtil
{
	public static Func<TValue, Task> AsNonRenderingEventHandler<TValue>(Func<TValue, Task> callback) =>
		new AsyncReceiver<TValue>(callback).Invoke;

	private sealed record AsyncReceiver<TValue>(Func<TValue, Task> Callback) : IHandleEvent
	{
		public Task Invoke(TValue value) => Callback(value);

		public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
	}
}
