using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>A plugin read from disk, with its module ready to be added to the host.</summary>
public sealed record LoadedPlugin(PluginInfo Info, IMokatermModule Module);
