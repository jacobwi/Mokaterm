using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>How a paged tool asks a page's descriptor whether the page works here.</summary>
public static class ToolPageRules
{
	/// <summary>The reason a page is greyed with when its own rule failed.</summary>
	public const string RuleFailed = "This page could not tell whether it works here.";

	/// <summary>
	/// The rule's answer for <paramref name="context"/>, or null when there is no rule. Plugins write rules too, so one
	/// that throws greys its own page with <see cref="RuleFailed"/> instead of taking the tool down.
	/// </summary>
	public static string? Evaluate<TContext>(Func<TContext, string?>? rule, TContext context, ILogger logger, string pageId)
	{
		ArgumentNullException.ThrowIfNull(logger);
		if (rule is null)
		{
			return null;
		}

		try
		{
			return rule(context);
		}
		catch (Exception ex)
		{
			logger.LogWarning("The availability rule of the page {Page} failed: {Reason}", pageId, LogSafe.Describe(ex));
			return RuleFailed;
		}
	}
}
