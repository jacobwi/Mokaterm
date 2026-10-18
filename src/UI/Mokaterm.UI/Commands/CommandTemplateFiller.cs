using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.UI.Interaction;

namespace Mokaterm.UI.Commands;

/// <summary>
/// Asks for the values a command's placeholders need and fills them in, for every place a command is typed or run.
/// The last answers are kept in memory for the window, per command text, so asking again starts from them. Values can
/// be secrets: nothing here is written anywhere, and locking the vault forgets them and cancels a question still open.
/// </summary>
internal sealed class CommandTemplateFiller : IDisposable
{
	/// <summary>How many commands' answers are kept; the oldest goes first.</summary>
	private const int MaxRemembered = 32;

	private readonly UserInteractionService _interaction;
	private readonly IVault _vault;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _remembered = new(StringComparer.Ordinal);
	private readonly LinkedList<string> _order = new();
	private CancellationTokenSource _unlocked = new();
	private bool _disposed;

	public CommandTemplateFiller(UserInteractionService interaction, IVault vault)
	{
		_interaction = interaction;
		_vault = vault;
		_vault.StatusChanged += OnVaultStatusChanged;
	}

	/// <summary>
	/// The command with its placeholders filled in, or null when the user cancelled or the vault locked meanwhile. A
	/// command without placeholders comes back at once, with any <c>{{{{</c> read as <c>{{</c>.
	/// </summary>
	/// <param name="confirmText">The prompt's primary button, which says what happens next.</param>
	public async Task<string?> FillAsync(CommandTemplate template, string confirmText, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(template);
		if (!template.HasPlaceholders)
		{
			return template.Fill(null);
		}

		CancellationToken unlocked;
		IReadOnlyDictionary<string, string> start;
		lock (_gate)
		{
			if (_disposed)
			{
				return null;
			}

			unlocked = _unlocked.Token;
			start = _remembered.GetValueOrDefault(template.Text) ?? new Dictionary<string, string>(StringComparer.Ordinal);
		}

		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, unlocked);
		IReadOnlyDictionary<string, string>? values = await _interaction.PromptPlaceholdersAsync(
			new PlaceholderPrompt
			{
				Title = "Fill in the command",
				Template = template,
				Values = start,
				ConfirmText = confirmText,
			},
			linked.Token);

		if (values is null || unlocked.IsCancellationRequested)
		{
			return null;
		}

		Remember(template.Text, values, unlocked);
		return template.Fill(values);
	}

	public void Dispose()
	{
		CancellationTokenSource unlocked;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			unlocked = _unlocked;
			Forget();
		}

		_vault.StatusChanged -= OnVaultStatusChanged;
		unlocked.Cancel();
		unlocked.Dispose();
	}

	private void Remember(string text, IReadOnlyDictionary<string, string> values, CancellationToken unlocked)
	{
		lock (_gate)
		{
			// An answer given just as the vault locked belongs to the session that ended.
			if (_disposed || unlocked.IsCancellationRequested)
			{
				return;
			}

			if (_remembered.Remove(text))
			{
				_ = _order.Remove(text);
			}

			_remembered[text] = new Dictionary<string, string>(values, StringComparer.Ordinal);
			_ = _order.AddLast(text);
			while (_order.Count > MaxRemembered && _order.First is { } oldest)
			{
				_ = _remembered.Remove(oldest.Value);
				_order.RemoveFirst();
			}
		}
	}

	private void Forget()
	{
		_remembered.Clear();
		_order.Clear();
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		if (status == VaultStatus.Unlocked)
		{
			return;
		}

		CancellationTokenSource locked;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			Forget();
			locked = _unlocked;
			_unlocked = new CancellationTokenSource();
		}

		// Not disposed: a question that read its token just before the lock may still be linking to it, and a source
		// whose token never handed out a wait handle holds nothing to release.
		locked.Cancel();
	}
}
