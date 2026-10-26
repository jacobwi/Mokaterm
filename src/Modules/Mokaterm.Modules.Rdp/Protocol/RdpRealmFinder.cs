namespace Mokaterm.Modules.Rdp.Protocol;

internal delegate Task<RdpRealm?> RdpRealmFinder(string domain, string host, CancellationToken cancellationToken);
