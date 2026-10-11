namespace Mokaterm.Modules.Ssh.Admin.Accounts;

/// <summary>One group entry with the members it lists. Users whose primary group it is are not listed by the group file.</summary>
internal sealed record GroupAccount(string Name, long Gid, IReadOnlyList<string> Members);
