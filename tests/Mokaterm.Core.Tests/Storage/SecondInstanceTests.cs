using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Storage;

/// <summary>
/// Two service providers over one data directory, which is what two copies of the desktop app started side by side
/// amount to: they share the files and nothing else, so no lock, cache or change event reaches across.
/// </summary>
public sealed class SecondInstanceTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task HeaderWrite_InAnInstanceStartedEarlier_DoesNotUndoAPasswordChange()
	{
		await using CoreTestContext first = new();
		await using CoreTestContext second = new(services => services.AddSingleton<IDeviceKeyProtector>(new FakeDeviceKeyProtector()), first.DataDirectory);
		await using AsyncServiceScope firstScope = await first.CreateVaultScopeAsync();
		await using AsyncServiceScope secondScope = await second.UnlockedScopeAsync();

		Assert.True((await firstScope.ServiceProvider.GetRequiredService<IVault>().ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "changed password", Ct)).Succeeded);
		await secondScope.ServiceProvider.GetRequiredService<IVault>().SetDeviceUnlockAsync(true, Ct);

		await using CoreTestContext third = new(dataDirectory: first.DataDirectory);
		await using AsyncServiceScope thirdScope = third.CreateScope();
		IVault vault = thirdScope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);
		Assert.True(vault.IsDeviceUnlockEnabled);
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Status);
		Assert.True((await vault.UnlockAsync("changed password", Ct)).Succeeded);
	}

	[Fact]
	public async Task Unlock_InAnInstanceStartedEarlier_UsesThePasswordSetInTheOther()
	{
		await using CoreTestContext first = new();
		await using CoreTestContext second = new(dataDirectory: first.DataDirectory);
		await using AsyncServiceScope firstScope = await first.CreateVaultScopeAsync();
		await using AsyncServiceScope secondScope = await second.UnlockedScopeAsync();
		IVault secondVault = secondScope.ServiceProvider.GetRequiredService<IVault>();

		Assert.True((await firstScope.ServiceProvider.GetRequiredService<IVault>().ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "changed password", Ct)).Succeeded);
		secondVault.Lock();

		Assert.Equal(UnlockStatus.InvalidPassword, (await secondVault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Status);
		Assert.True((await secondVault.UnlockAsync("changed password", Ct)).Succeeded);
	}

	[Fact]
	public async Task SaveHost_InAnInstanceHoldingAnOlderCopy_KeepsTheHostTheOtherSaved()
	{
		await using CoreTestContext first = new();
		await using CoreTestContext second = new(dataDirectory: first.DataDirectory);
		await using AsyncServiceScope firstScope = await first.CreateVaultScopeAsync();
		await using AsyncServiceScope secondScope = await second.UnlockedScopeAsync();
		IConnectionRepository firstRepository = firstScope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		IConnectionRepository secondRepository = secondScope.ServiceProvider.GetRequiredService<IConnectionRepository>();
		Assert.Empty((await secondRepository.GetCatalogAsync(Ct)).Hosts);

		await firstRepository.SaveHostAsync(Host("10.0.0.1"), Ct);
		await secondRepository.SaveHostAsync(Host("10.0.0.2"), Ct);

		await using AsyncServiceScope reader = await first.UnlockedScopeAsync();
		ConnectionCatalog catalog = await reader.ServiceProvider.GetRequiredService<IConnectionRepository>().GetCatalogAsync(Ct);
		Assert.Equal(["10.0.0.1", "10.0.0.2"], catalog.Hosts.Select(host => host.Address).Order(StringComparer.Ordinal));
		Assert.Equal(2, (await secondRepository.GetCatalogAsync(Ct)).Hosts.Count);
	}

	private static HostProfile Host(string address) => new() { Id = Guid.NewGuid(), Address = address };
}
