using Microsoft.AspNetCore.Components;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// A tool that needs no session, such as a subnet calculator: the shell lists it in the top bar's Tools menu and the
/// command palette, and opens it in a dialog of its own. Register it with <c>AddAppTool</c>.
/// </summary>
/// <remarks>
/// It renders inside the shell's error boundary, so a failure shows in the dialog and leaves the rest of the app alone.
/// Services come by injection like any component's; the tool stays open across tab switches and closes when the vault
/// locks.
/// </remarks>
public abstract class AppToolBase : ComponentBase;
