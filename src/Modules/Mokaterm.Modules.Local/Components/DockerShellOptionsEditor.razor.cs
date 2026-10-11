using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.Modules.Local.Components;

/// <summary>
/// The Docker container part of the connection editor: which container, on which daemon, as whom, where, and what runs
/// there. Each field says what is wrong with it as it is typed, in the words the connect would fail with.
/// </summary>
public partial class DockerShellOptionsEditor : ConnectionOptionsEditorBase<DockerShellOptions>
{
	private string? ContainerError => Problem(Current.Container, DockerNames.ContainerProblem);

	private string? ContextError => Problem(Current.Context, ContainerNames.DockerContextProblem);

	private string? HostError =>
		ContainerNames.Value(Current.Context) is not null && ContainerNames.Value(Current.Host) is not null
			? "Name a context or a host, not both."
			: Problem(Current.Host, ContainerNames.DockerHostProblem);

	private string? UserError => Problem(Current.User, ContainerNames.UserProblem);

	private string? FolderError => Problem(Current.Folder, ContainerNames.FolderProblem);

	protected override DockerShellOptions From(ProtocolOptions options) => DockerShellOptions.From(options);

	protected override ProtocolOptions ApplyTo(DockerShellOptions current, ProtocolOptions options) => current.ApplyTo(options);

	/// <summary>An empty field is not a problem: it leaves the choice to docker or to the host's address.</summary>
	private static string? Problem(string? value, Func<string?, string?> rule) =>
		ContainerNames.Value(value) is { } trimmed ? rule(trimmed) : null;

	/// <summary>Lists against what the fields say now, so a context typed a moment ago is the one asked.</summary>
	private Task<ContainerChoiceList> ListAsync(CancellationToken cancellationToken) =>
		ContainerListing.DockerAsync(Current, LocalMachine.Current(), LocalCommandRunner.Instance, cancellationToken);

	private Task OnPickAsync(ContainerChoice choice) => ChangeAsync(Current with { Container = choice.Name });

	private Task OnContainerChangedAsync(string? value) => ChangeAsync(Current with { Container = value });

	private Task OnContextChangedAsync(string? value) => ChangeAsync(Current with { Context = value });

	private Task OnHostChangedAsync(string? value) => ChangeAsync(Current with { Host = value });

	private Task OnUserChangedAsync(string? value) => ChangeAsync(Current with { User = value });

	private Task OnFolderChangedAsync(string? value) => ChangeAsync(Current with { Folder = value });

	private Task OnShellChangedAsync(ContainerShell value) => ChangeAsync(Current with { Shell = value });

	private Task OnCommandChangedAsync(string? value) => ChangeAsync(Current with { Command = value });
}
