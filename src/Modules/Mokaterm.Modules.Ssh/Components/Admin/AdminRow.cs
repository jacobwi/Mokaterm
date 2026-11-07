namespace Mokaterm.Modules.Ssh.Components.Admin;

// Index is the item's place in what the page read (a crontab line, a rule's position, a log line), not where it is
// drawn, so it keys and selects the row whatever order the table shows.
internal sealed record AdminRow<T>(T Item, int Index, int Depth = 0);
