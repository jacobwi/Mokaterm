using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Where an app tool opens: by the same rule as a session tool, a window where the host has them and the setting asks
/// for one, else the dialog, which also catches a window that would not open. A window lives until it is closed; the
/// dialog goes when the vault locks.
/// </summary>
public sealed class AppToolActionsTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly AppToolDescriptor Subnets = new()
	{
		Id = "subnet-calculator",
		Title = "Subnet calculator",
		Icon = MokatermIcons.Globe,
		Description = "Network, broadcast and host range of a prefix",
		Component = typeof(NoAppTool),
	};

	private static readonly AppToolDescriptor Jwt = new()
	{
		Id = "jwt",
		Title = "JWT decoder",
		Icon = MokatermIcons.Key,
		Component = typeof(NoAppTool),
		Size = SessionToolSize.Wide,
	};

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task NoWindowHost_OpensTheDialog_AndAnotherToolTakesItsPlace()
	{
		await using ToolHarness harness = await CreateAsync(windows: null);
		int changes = 0;
		harness.AppTools.Changed += () => changes++;

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		Assert.Same(Subnets, harness.AppTools.Dialog);
		Assert.Equal(1, changes);
		Assert.False(harness.AppTools.CanOpenWindows);

		await harness.AppTools.OpenAsync(Jwt).WaitAsync(Patience, Ct);
		Assert.Same(Jwt, harness.AppTools.Dialog);
		Assert.True(harness.AppTools.IsOpen(Jwt));
		Assert.False(harness.AppTools.IsOpen(Subnets));

		harness.AppTools.CloseDialog();
		Assert.Null(harness.AppTools.Dialog);
		Assert.False(harness.AppTools.AnyOpen);
	}

	[Fact]
	public async Task WindowHost_SettingOn_OpensAWindowTitledWithTheToolAndMokaterm()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);

		await harness.AppTools.OpenAsync(Jwt).WaitAsync(Patience, Ct);

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Equal("JWT decoder · Mokaterm", request.Title);
		Assert.Equal(ToolWindowTarget.AppTool("jwt"), request.Target);
		Assert.True(request.Target.IsAppTool);
		Assert.Equal(SessionToolSize.Wide, request.Size);
		Assert.Same(harness.Services, request.Services);
		Assert.Null(harness.AppTools.Dialog);
		Assert.True(harness.AppTools.IsInWindow(Jwt));
		Assert.True(harness.AppTools.AnyOpen);
	}

	[Fact]
	public async Task WindowHost_SettingOff_KeepsTheDialog_UntilItPopsOut()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		Assert.Empty(windows.Opened);
		Assert.Same(Subnets, harness.AppTools.Dialog);

		await harness.AppTools.PopOutAsync(Subnets).WaitAsync(Patience, Ct);
		Assert.Single(windows.Opened);
		Assert.Null(harness.AppTools.Dialog);
		Assert.True(harness.AppTools.IsInWindow(Subnets));
	}

	// A tool already in a window comes forward there, whatever the setting says now.
	[Fact]
	public async Task AToolWithAWindow_OpensThereAgain_EvenWithTheSettingOff()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.AppTools.PopOutAsync(Subnets).WaitAsync(Patience, Ct);

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);

		Assert.Null(harness.AppTools.Dialog);
		Assert.Single(windows.Shown);
	}

	[Fact]
	public async Task AWindowThatWillNotOpen_FallsBackToTheDialog()
	{
		FakeToolWindows windows = new() { Opens = false };
		await using ToolHarness harness = await CreateAsync(windows);

		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);

		Assert.Same(Subnets, harness.AppTools.Dialog);
	}

	[Fact]
	public async Task ReturnToDialog_ClosesTheWindowAsTheUsersOwn_AndBringsTheMainWindowForward()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		List<ToolWindowTarget> closedByUser = [];
		harness.Windows.ClosedByUser += closedByUser.Add;

		harness.AppTools.ReturnToDialog(Subnets);

		Assert.Same(Subnets, harness.AppTools.Dialog);
		Assert.False(harness.AppTools.IsInWindow(Subnets));
		Assert.Equal(1, windows.OwnerShown);
		Assert.Equal([ToolWindowTarget.AppTool("subnet-calculator")], closedByUser);
	}

	[Fact]
	public async Task LockingTheVault_ClosesTheDialog_AndThisWindowsToolWindows()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows, new AppearanceSettings { ToolsInOwnWindow = false });
		await harness.AppTools.OpenAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.AppTools.PopOutAsync(Subnets).WaitAsync(Patience, Ct);
		await harness.AppTools.OpenAsync(Jwt).WaitAsync(Patience, Ct);

		harness.Vault.Lock();

		Assert.Null(harness.AppTools.Dialog);
		Assert.Contains(harness.Services, windows.ClosedOwners);
		Assert.Empty(windows.Shown);
	}

	// A window is remembered by the tool's id, which a plugin removed since may no longer answer to.
	[Fact]
	public async Task Find_ByTheIdItWasRegisteredUnder_AndNothingForAnIdNoOneHas()
	{
		await using ToolHarness harness = await CreateAsync(windows: null);

		Assert.Same(Jwt, harness.AppTools.Find("jwt"));
		Assert.Null(harness.AppTools.Find("password-generator"));
	}

	private static Task<ToolHarness> CreateAsync(FakeToolWindows? windows, AppearanceSettings? appearance = null) =>
		ToolHarness.CreateAsync(windows, appearance, appTools: [Subnets, Jwt]);

	private sealed class NoAppTool : AppToolBase;
}
