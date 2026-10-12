using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>
/// A dialog service with no page behind it: a component dialog is handed to <see cref="Answer"/> with the parameters it
/// would have rendered with, which can call them the way the user would and returns what the dialog closes with.
/// </summary>
internal sealed class ScriptedDialogs : IMokaDialogService
{
	private readonly List<ShownDialog> _shown = [];

	// Nothing here goes through a request: component dialogs are answered as they are opened.
	public event Action<MokaDialogRequest>? OnDialogRequested
	{
		add { }
		remove { }
	}

	public event Action? OnDialogClosed;

	/// <summary>Plays the user; the default closes every dialog as cancelled.</summary>
	public Func<ShownDialog, Task<object?>> Answer { get; set; } = _ => Task.FromResult<object?>(null);

	/// <summary>Every component dialog opened, in order.</summary>
	public IReadOnlyList<ShownDialog> Shown => _shown;

	public IReadOnlyList<MokaDialogRequest> OpenDialogs => [];

	public async Task<object?> ShowComponentAsync<TComponent>(
		string title,
		Action<Dictionary<string, object>>? parameters = null,
		Action<MokaDialogOptions>? configure = null) where TComponent : IComponent
	{
		Dictionary<string, object> values = new(StringComparer.Ordinal);
		parameters?.Invoke(values);
		MokaDialogOptions options = new();
		configure?.Invoke(options);
		ShownDialog dialog = new(typeof(TComponent), title, values, options);
		_shown.Add(dialog);
		object? result = await Answer(dialog);
		OnDialogClosed?.Invoke();
		return result;
	}

	public Task<bool> ConfirmAsync(string message, string? title = null, Action<MokaDialogOptions>? configure = null) =>
		throw new NotSupportedException();

	public Task<string?> PromptAsync(string message, string? title = null, string? defaultValue = null) =>
		throw new NotSupportedException();

	public Task<string?> PromptAsync(string message, string? title, string? defaultValue, Action<MokaDialogOptions>? configure) =>
		throw new NotSupportedException();

	public Task ShowAsync(string title, RenderFragment content, Action<MokaDialogOptions>? configure = null) =>
		throw new NotSupportedException();

	public void Close(bool result = false) => throw new NotSupportedException();

	public void CloseWithResult(object? result) => throw new NotSupportedException();

	public void Close(MokaDialogRequest request, bool result = false) => throw new NotSupportedException();

	public void CloseWithResult(MokaDialogRequest request, object? result) => throw new NotSupportedException();

	public void CloseAll() => throw new NotSupportedException();

	/// <summary>One component dialog as it was asked for.</summary>
	internal sealed record ShownDialog(Type Component, string Title, IReadOnlyDictionary<string, object> Parameters, MokaDialogOptions Options)
	{
		public T Parameter<T>(string name) => (T)Parameters[name];
	}
}
