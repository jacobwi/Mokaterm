namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// What a tool window shows: a session tool on one session, or an app tool, which needs no session. One window per
/// target: opening a target that already has a window brings that window forward.
/// </summary>
public sealed record ToolWindowTarget
{
	private ToolWindowTarget(Guid? sessionId, string toolKey)
	{
		SessionId = sessionId;
		ToolKey = toolKey;
	}

	/// <summary>The session a session tool works on; null for an app tool.</summary>
	public Guid? SessionId { get; }

	/// <summary>A session tool's <c>SessionToolDescriptor.Key</c>, or an app tool's <c>AppToolDescriptor.Id</c>.</summary>
	public string ToolKey { get; }

	public bool IsAppTool => SessionId is null;

	/// <summary>
	/// The name the window's place is remembered under. An app tool's id goes after <c>app:</c>, so an app tool and a
	/// session tool that happen to share a name keep places of their own; a session tool keeps the bare key it always had.
	/// </summary>
	public string PlaceKey => IsAppTool ? "app:" + ToolKey : ToolKey;

	/// <summary>A session tool, such as Docker on one session.</summary>
	public static ToolWindowTarget SessionTool(Guid sessionId, string toolKey)
	{
		ArgumentException.ThrowIfNullOrEmpty(toolKey);
		return new(sessionId, toolKey);
	}

	/// <summary>An app tool, such as the subnet calculator.</summary>
	public static ToolWindowTarget AppTool(string toolId)
	{
		ArgumentException.ThrowIfNullOrEmpty(toolId);
		return new(null, toolId);
	}
}
