// Mokaterm.UI.Common's JavaScript initializer, which Blazor loads at startup. The listener that starts clipboard writes
// inside the click has to be in place before the first click, long before .NET imports clipboard.js for its first copy.
import { listenForCopies } from './js/clipboard.js';

// Blazor Web Apps: the web host and the DevHost.
export function afterWebStarted() {
	listenForCopies();
}

// Blazor Hybrid: the desktop app's windows.
export function afterStarted() {
	listenForCopies();
}
