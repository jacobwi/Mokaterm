using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moka.Red.Feedback.Extensions;
using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Extensions;
using Mokaterm.UI.Interaction;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Which notices the bell keeps, what it calls them, how many it holds, and what every toast does by default: a repeat
/// counts instead of stacking and an error stays longer.
/// </summary>
public sealed class NoticeTests
{
	[Theory]
	[InlineData(NoticeSeverity.Warning, true)]
	[InlineData(NoticeSeverity.Error, true)]
	[InlineData(NoticeSeverity.Info, false)]
	[InlineData(NoticeSeverity.Success, false)]
	public void OnlyWarningsAndErrors_AreKept(NoticeSeverity severity, bool kept) =>
		Assert.Equal(kept, new PendingNotice(severity, "message", null).IsKept);

	[Fact]
	public void AKeptNotice_TakesItsTitle_OrOneByItsSeverity()
	{
		PendingNotice titled = new(NoticeSeverity.Warning, "restarted 4 times", "Docker on build");
		PendingNotice untitled = new(NoticeSeverity.Error, "could not be saved", null);

		Assert.Equal("Docker on build", titled.KeptTitle);
		Assert.Equal(MokaToastSeverity.Warning, titled.ToastSeverity);
		Assert.Equal("Error", untitled.KeptTitle);
		Assert.Equal(MokaToastSeverity.Error, untitled.ToastSeverity);
	}

	[Fact]
	public void TheShell_CapsTheBell_AndGivesEveryToastItsDefaults()
	{
		ServiceCollection services = new();
		services.AddMokatermUI();
		using ServiceProvider provider = services.BuildServiceProvider();
		MokaFeedbackOptions feedback = provider.GetRequiredService<IOptions<MokaFeedbackOptions>>().Value;
		MokaToastOptions error = new() { Severity = MokaToastSeverity.Error };
		MokaToastOptions info = new() { Severity = MokaToastSeverity.Info };

		feedback.ToastDefaults!(error);
		feedback.ToastDefaults(info);

		Assert.Equal(100, feedback.NotificationLimit);
		Assert.Equal(MokaToastRepeats.Count, error.Repeats);
		Assert.Equal(TimeSpan.FromSeconds(8), error.Duration);
		Assert.Equal(MokaToastRepeats.Count, info.Repeats);
		Assert.Equal(new MokaToastOptions().Duration, info.Duration);
	}

	[Fact]
	public void TheRelay_HandsANoticeToWhoeverListens()
	{
		NoticeRelay relay = new();
		List<PendingNotice> heard = [];
		relay.Kept += heard.Add;
		PendingNotice notice = new(NoticeSeverity.Error, "failed", "Docker");

		relay.Keep(notice);

		Assert.Equal([notice], heard);
	}
}
