using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.Vault;

/// <summary>
/// Tells the user when a vault document could not be read and was set aside, which otherwise shows only as a list that is
/// suddenly empty. Like every notice, it waits for the vault to be unlocked, so the path never shows on the lock screen.
/// </summary>
internal sealed class DamagedDocumentNotices : IDisposable
{
	private const string Cause = "It was damaged or written by a newer version of Mokaterm";

	private readonly IVaultDataStore _store;
	private readonly IUserInteraction _interaction;
	private readonly Lock _gate = new();
	private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
	private bool _watching;

	public DamagedDocumentNotices(IVaultDataStore store, IUserInteraction interaction)
	{
		_store = store;
		_interaction = interaction;
	}

	/// <summary>Starts listening. The shell calls it once when it starts, before anything reads the vault.</summary>
	public void Start()
	{
		lock (_gate)
		{
			if (_watching)
			{
				return;
			}

			_watching = true;
		}

		_store.DocumentDamaged += OnDocumentDamaged;
	}

	public void Dispose() => _store.DocumentDamaged -= OnDocumentDamaged;

	/// <summary>The notice for <paramref name="document"/>: what was lost, what that means, and where the damaged copy is.</summary>
	internal static (string Title, string Message) Describe(DamagedDocument document)
	{
		(string title, string consequence) = document.Name switch
		{
			"connections" => ("Saved hosts could not be read", "Hosts, logins and folders start empty."),
			"credentials" => ("Saved passwords and keys could not be read", "The keychain starts empty, and logins ask for their passwords again."),
			"known-hosts" => ("Trusted host keys could not be read", "Every host asks to be trusted again."),
			"commands" => ("Saved commands could not be read", "The saved commands start empty."),
			_ => ($"The vault document \"{document.Name}\" could not be read", "What it held starts empty."),
		};

		string message = document.MovedAside
			? $"{Cause}, so it was set aside. {consequence} The damaged copy is at {document.Path}"
			: $"{Cause}. {consequence} It could not be moved aside, so the next change overwrites it: copy {document.Path} somewhere safe first.";
		return (title, message);
	}

	private void OnDocumentDamaged(DamagedDocument document)
	{
		lock (_gate)
		{
			// A copy that could not be moved fails again on every read; hearing about it once is enough.
			if (!_shown.Add(document.Path))
			{
				return;
			}
		}

		(string title, string message) = Describe(document);
		_interaction.Notify(NoticeSeverity.Error, message, title);
	}
}
