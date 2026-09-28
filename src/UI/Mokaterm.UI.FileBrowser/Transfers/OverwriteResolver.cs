using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.UI.FileBrowser.Transfers;

internal enum OverwriteChoice
{
	Overwrite,
	Skip,
	Cancel,
}

/// <summary>
/// Decides what happens to name conflicts during one batch, following <see cref="FileTransferSettings.Overwrite"/>.
/// "Overwrite all" and "Skip all" answers hold for the rest of the batch.
/// </summary>
internal sealed class OverwriteResolver
{
	private readonly IUserInteraction _interaction;
	private readonly OverwriteBehavior _behavior;
	private readonly bool _isBatch;
	private OverwriteChoice? _remembered;

	public OverwriteResolver(IUserInteraction interaction, OverwriteBehavior behavior, bool isBatch)
	{
		_interaction = interaction;
		_behavior = behavior;
		_isBatch = isBatch;
	}

	/// <summary>False when every conflict is overwritten anyway, so looking for existing files is wasted work.</summary>
	public bool ChecksExisting => _behavior != OverwriteBehavior.Overwrite;

	public async Task<OverwriteChoice> ResolveAsync(
		string path,
		RemoteFileEntry existing,
		long? incomingSize,
		DateTimeOffset? incomingModified,
		CancellationToken cancellationToken)
	{
		switch (_behavior)
		{
			case OverwriteBehavior.Overwrite:
				return OverwriteChoice.Overwrite;
			case OverwriteBehavior.Skip:
				return OverwriteChoice.Skip;
			default:
				break;
		}

		if (_remembered is { } remembered)
		{
			return remembered;
		}

		OverwriteDecision decision = await _interaction.ConfirmOverwriteAsync(
			new OverwritePrompt
			{
				Path = path,
				ExistingSize = existing.Kind == RemoteEntryKind.File ? existing.Size : null,
				ExistingModified = existing.LastModified,
				IncomingSize = incomingSize,
				IncomingModified = incomingModified,
				IsBatch = _isBatch,
			},
			cancellationToken);

		switch (decision)
		{
			case OverwriteDecision.OverwriteAll:
				_remembered = OverwriteChoice.Overwrite;
				return OverwriteChoice.Overwrite;
			case OverwriteDecision.SkipAll:
				_remembered = OverwriteChoice.Skip;
				return OverwriteChoice.Skip;
			case OverwriteDecision.Overwrite:
				return OverwriteChoice.Overwrite;
			case OverwriteDecision.Skip:
				return OverwriteChoice.Skip;
			default:
				return OverwriteChoice.Cancel;
		}
	}
}
