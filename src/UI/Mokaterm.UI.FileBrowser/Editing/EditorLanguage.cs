namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// A language the editor colours. <see cref="Id"/> is the key the page's tokenizers in <c>syntax.js</c> use, so the
/// two lists have to name the same languages.
/// </summary>
internal sealed record EditorLanguage(string Id, string Name);
