using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moka.Red.ContextMenu;
using Moka.Red.Feedback.Dialog;
using Moka.Red.Feedback.Toast;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Maui.ToolWindows;
using Mokaterm.UI.Commands;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Extensions;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Tools;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// A tool window borrows the window's vault, sessions and shell state and gets its own of everything tied to a page.
/// Run against the shell's real registrations, so a service that changes sides shows up here before it shows up as a
/// second vault in a tool window. The class is the desktop host's, compiled in here because the host cannot be
/// referenced.
/// </summary>
public sealed class ToolPageServicesTests
{
	[Theory]
	[InlineData(typeof(IJSRuntime))]
	[InlineData(typeof(NavigationManager))]
	[InlineData(typeof(IMokaDialogService))]
	[InlineData(typeof(IMokaToastService))]
	[InlineData(typeof(IMokaContextMenuService))]
	[InlineData(typeof(IClipboardService))]
	[InlineData(typeof(ShellInterop))]
	[InlineData(typeof(FormInterop))]
	[InlineData(typeof(IUserInteraction))]
	[InlineData(typeof(UserInteractionService))]
	public void PageServices_AreTheToolWindowsOwn(Type service) => Assert.True(Classified().IsPerPage(service), service.Name);

	// Built over a JS wrapper or the page's dialogs, so a tool window that resolved one would get one wired to its own
	// page: a tool's editor and file browser open in the tool's window, and a link it opens leaves from there.
	[Theory]
	[InlineData(typeof(ShellInputBridge))]
	[InlineData(typeof(ConnectionActions))]
	[InlineData(typeof(IRemoteTextEditor))]
	[InlineData(typeof(IFileBrowserDialog))]
	[InlineData(typeof(IUrlOpener))]
	public void ServicesBuiltOverThePage_AreTheToolWindowsOwnToo(Type service) => Assert.True(Classified().IsPerPage(service), service.Name);

	[Theory]
	[InlineData(typeof(IVault))]
	[InlineData(typeof(ISessionManager))]
	[InlineData(typeof(SessionWorkspace))]
	[InlineData(typeof(ISessionTabs))]
	[InlineData(typeof(SessionToolActions))]
	[InlineData(typeof(ShellToolWindows))]
	[InlineData(typeof(AppToolActions))]
	[InlineData(typeof(ToolWindowRestore))]
	[InlineData(typeof(SessionRestore))]
	[InlineData(typeof(UiStateStore))]
	[InlineData(typeof(ShellState))]
	[InlineData(typeof(InputBroadcast))]
	[InlineData(typeof(CatalogState))]
	[InlineData(typeof(CommandSnippetActions))]
	[InlineData(typeof(SessionViewRequests))]
	public void WindowState_IsBorrowed(Type service) => Assert.False(Classified().IsPerPage(service), service.Name);

	// The session manager takes the prompt queue to ask for passwords. The queue being the page's must not make the
	// session manager the page's as well, or a tool window would start with no sessions at all.
	[Fact]
	public void TakingThePromptQueue_DoesNotMakeAServiceThePages()
	{
		ToolPageServices pages = Classified();

		Assert.True(pages.IsPerPage(typeof(IUserInteraction)));
		Assert.Null(pages.FirstNotShared([typeof(IVault), typeof(ISessionManager)]));
	}

	[Fact]
	public void AVaultThatCallsIntoThePage_IsReported_SoNoWindowOpensWithASecondOne()
	{
		ServiceCollection services = Registrations();
		services.AddScoped<IVault, PageBoundVault>();

		ToolPageServices pages = new(services, MokatermUiServiceCollectionExtensions.PageServices);

		Assert.Equal(typeof(IVault), pages.FirstNotShared([typeof(IVault), typeof(ISessionManager)]));
	}

	[Fact]
	public async Task Borrow_GivesTheOwnersInstances_AndFreshPageOnes()
	{
		ServiceCollection services = Registrations();
		await using ServiceProvider root = services.BuildServiceProvider();
		await using AsyncServiceScope owner = root.CreateAsyncScope();
		IServiceProvider borrowed = new ToolPageServices(services, MokatermUiServiceCollectionExtensions.PageServices).Borrow(owner.ServiceProvider);
		await using AsyncServiceScope page = borrowed.CreateAsyncScope();

		Assert.Same(owner.ServiceProvider.GetRequiredService<IVault>(), page.ServiceProvider.GetRequiredService<IVault>());
		Assert.Same(owner.ServiceProvider.GetRequiredService<ISessionManager>(), page.ServiceProvider.GetRequiredService<ISessionManager>());
		Assert.NotSame(owner.ServiceProvider.GetRequiredService<IJSRuntime>(), page.ServiceProvider.GetRequiredService<IJSRuntime>());
		Assert.NotSame(owner.ServiceProvider.GetRequiredService<IUserInteraction>(), page.ServiceProvider.GetRequiredService<IUserInteraction>());

		// The page's prompt queue is one object under both names, as it is in the window.
		Assert.Same(page.ServiceProvider.GetRequiredService<UserInteractionService>(), page.ServiceProvider.GetRequiredService<IUserInteraction>());
	}

	[Fact]
	public async Task ClosingThePage_DisposesItsOwnServices_NeverTheBorrowedOnes()
	{
		ServiceCollection services = Registrations();
		await using ServiceProvider root = services.BuildServiceProvider();
		await using AsyncServiceScope owner = root.CreateAsyncScope();
		IServiceProvider borrowed = new ToolPageServices(services, MokatermUiServiceCollectionExtensions.PageServices).Borrow(owner.ServiceProvider);
		TrackedVault vault = (TrackedVault)owner.ServiceProvider.GetRequiredService<IVault>();
		TrackedJs js;

		await using (AsyncServiceScope page = borrowed.CreateAsyncScope())
		{
			Assert.Same(vault, page.ServiceProvider.GetRequiredService<IVault>());
			js = (TrackedJs)page.ServiceProvider.GetRequiredService<IJSRuntime>();
		}

		Assert.True(js.Disposed);
		Assert.False(vault.Disposed);
	}

	// The main window's chrome is an app-wide singleton like any other, so without this a tool window's header would hand
	// its drag regions to the main window.
	[Fact]
	public async Task AWindowsOwnChrome_ReplacesTheMainWindows_InThatWindowOnly()
	{
		ServiceCollection services = Registrations();
		MainChrome main = new();
		services.AddSingleton<MainChrome>(main);
		services.AddSingleton<IWindowChrome>(provider => provider.GetRequiredService<MainChrome>());
		await using ServiceProvider root = services.BuildServiceProvider();
		await using AsyncServiceScope owner = root.CreateAsyncScope();
		ToolPageServices pages = new(services, MokatermUiServiceCollectionExtensions.PageServices);
		MainChrome own = new();

		IServiceProvider borrowed = pages.Borrow(owner.ServiceProvider, new Dictionary<Type, object> { [typeof(IWindowChrome)] = own });
		IServiceProvider plain = pages.Borrow(owner.ServiceProvider);
		await using AsyncServiceScope page = borrowed.CreateAsyncScope();

		Assert.Same(own, page.ServiceProvider.GetRequiredService<IWindowChrome>());
		Assert.Same(own, Assert.Single(page.ServiceProvider.GetServices<IWindowChrome>()));
		Assert.Same(main, owner.ServiceProvider.GetRequiredService<IWindowChrome>());
		Assert.Same(main, plain.GetRequiredService<IWindowChrome>());
		Assert.Same(owner.ServiceProvider.GetRequiredService<IVault>(), page.ServiceProvider.GetRequiredService<IVault>());
	}

	[Fact]
	public async Task ServicesRegisteredSeveralTimes_KeepEveryInstanceInOrder()
	{
		ServiceCollection services = Registrations();
		services.AddSingleton<IShape, Square>();
		services.AddSingleton<IShape, Circle>();
		await using ServiceProvider root = services.BuildServiceProvider();
		await using AsyncServiceScope owner = root.CreateAsyncScope();
		IServiceProvider borrowed = new ToolPageServices(services, MokatermUiServiceCollectionExtensions.PageServices).Borrow(owner.ServiceProvider);

		Assert.Equal(owner.ServiceProvider.GetServices<IShape>(), borrowed.GetServices<IShape>());
		Assert.IsType<Circle>(borrowed.GetRequiredService<IShape>());
	}

	private static ToolPageServices Classified() => new(Registrations(), MokatermUiServiceCollectionExtensions.PageServices);

	/// <summary>The shell's registrations, with stand-ins for Core, Sessions and the WebView's own page services.</summary>
	private static ServiceCollection Registrations()
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddMokatermUI();
		services.AddScoped<IJSRuntime, TrackedJs>();
		services.AddScoped<NavigationManager, IdleNavigation>();
		services.AddScoped<IVault, TrackedVault>();
		services.AddScoped<ISessionManager, PromptingSessionManager>();
		return services;
	}

	private interface IShape;

	private sealed class Square : IShape;

	private sealed class Circle : IShape;

	private sealed class TrackedJs : IJSRuntime, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();

		public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
			throw new NotSupportedException();
	}

	private sealed class IdleNavigation : NavigationManager;

	/// <summary>A window's chrome that does nothing but be told apart from another.</summary>
	private sealed class MainChrome : IWindowChrome
	{
		public event Action? InsetsChanged
		{
			add { }
			remove { }
		}

		public WindowChromeInsets Insets => default;

		public void SetDragRegions(IReadOnlyList<WindowDragRegion> regions)
		{
		}

		public void ResetDragRegions()
		{
		}
	}

	/// <summary>A session manager that asks for passwords, as the real one does.</summary>
	private sealed class PromptingSessionManager(IUserInteraction interaction) : ManualSessionManager
	{
		public IUserInteraction Interaction { get; } = interaction;
	}

	/// <summary>Takes nothing of the page, as the real vault does, and says whether it was disposed.</summary>
	private sealed class TrackedVault : UnlockedVault, IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	/// <summary>A vault built over the page's script, which no window may ever borrow a fresh copy of.</summary>
	private sealed class PageBoundVault(IJSRuntime js) : UnlockedVault
	{
		public IJSRuntime Js { get; } = js;
	}
}
