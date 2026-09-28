using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Core.Security;
using Mokaterm.Core.Serialization;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Security;

public sealed class VaultTests
{
	private const string DocumentName = "notes";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task LoadAsync_WithoutHeader_IsUninitialized()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		Assert.Equal(VaultStatus.Unknown, vault.Status);

		await vault.LoadAsync(Ct);

		Assert.Equal(VaultStatus.Uninitialized, vault.Status);
		Assert.Null(vault.LastLockReason);
	}

	[Fact]
	public async Task CreateAsync_LeavesVaultUnlockedAndWritesHeader()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);
		List<VaultStatus> statuses = [];
		vault.StatusChanged += statuses.Add;

		await vault.CreateAsync(CoreTestContext.MasterPassword, Ct);

		Assert.Equal(VaultStatus.Unlocked, vault.Status);
		Assert.Equal(VaultStatus.Unlocked, Assert.Single(statuses));
		JsonObject header = ReadHeader(context);
		Assert.Equal(1, (int)header["format"]!);
		Assert.True(Guid.TryParse((string)header["vaultId"]!, out _));
		Assert.Equal("argon2id", (string)header["kdf"]!["algorithm"]!);
		Assert.Equal(16, Convert.FromBase64String((string)header["kdf"]!["salt"]!).Length);
		Assert.Equal(12, Convert.FromBase64String((string)header["masterKey"]!["nonce"]!).Length);
		Assert.Equal(16, Convert.FromBase64String((string)header["masterKey"]!["tag"]!).Length);
		Assert.Equal(32, Convert.FromBase64String((string)header["masterKey"]!["ciphertext"]!).Length);
		Assert.Equal(32, Convert.FromBase64String((string)header["keyCheck"]!).Length);
		Assert.Null(header["device"]);
	}

	[Fact]
	public async Task CreateAsync_WhenVaultExists_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = context.CreateScope();
		IVault vault = second.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		await Assert.ThrowsAsync<InvalidOperationException>(() => vault.CreateAsync("another password", Ct));
		Assert.Equal(VaultStatus.Locked, vault.Status);
	}

	[Fact]
	public async Task UnlockAsync_CorrectPassword_UnlocksAndDecryptsExistingData()
	{
		await using CoreTestContext context = new();
		await using (AsyncServiceScope creator = await context.CreateVaultScopeAsync())
		{
			await creator.ServiceProvider.GetRequiredService<IVaultDataStore>().WriteAsync(DocumentName, new Note("kept"), Ct);
		}

		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);
		Assert.Equal(VaultStatus.Locked, vault.Status);

		UnlockResult result = await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct);

		Assert.Equal(UnlockResult.Success, result);
		Assert.Equal(VaultStatus.Unlocked, vault.Status);
		Note? note = await scope.ServiceProvider.GetRequiredService<IVaultDataStore>().ReadAsync<Note>(DocumentName, Ct);
		Assert.Equal("kept", note?.Text);
	}

	[Fact]
	public async Task UnlockAsync_WrongPassword_ReturnsInvalidPassword()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope creator = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		UnlockResult result = await vault.UnlockAsync("not the password", Ct);

		Assert.Equal(UnlockStatus.InvalidPassword, result.Status);
		Assert.Equal(VaultStatus.Locked, vault.Status);
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync("", Ct)).Status);
	}

	[Fact]
	public async Task UnlockAsync_WithoutVault_ReturnsVaultMissing()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		UnlockResult result = await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct);

		Assert.Equal(UnlockStatus.VaultMissing, result.Status);
	}

	[Theory]
	[InlineData("{ not json")]
	[InlineData("{\"format\": 2, \"vaultId\": \"8a1c7c62-3a5e-4d2b-9a55-0b5d8f3f1c11\"}")]
	public async Task UnlockAsync_UnreadableHeader_ReturnsCorrupted(string headerJson)
	{
		await using CoreTestContext context = new();
		await File.WriteAllTextAsync(context.HeaderPath, headerJson, Ct);
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		UnlockResult result = await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct);

		Assert.Equal(VaultStatus.Locked, vault.Status);
		Assert.Equal(UnlockStatus.Corrupted, result.Status);
		await Assert.ThrowsAsync<InvalidOperationException>(() => vault.CreateAsync("fresh password", Ct));
	}

	[Fact]
	public async Task UnlockAsync_UnknownFormatVersion_ReturnsCorrupted()
	{
		await using CoreTestContext context = new();
		await using (await context.CreateVaultScopeAsync())
		{
		}

		JsonObject header = ReadHeader(context);
		header["format"] = 2;
		await File.WriteAllTextAsync(context.HeaderPath, header.ToJsonString(), Ct);

		await using CoreTestContext reopened = new();
		File.Copy(context.HeaderPath, reopened.HeaderPath);
		await using AsyncServiceScope scope = reopened.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		Assert.Equal(UnlockStatus.Corrupted, (await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Status);
	}

	[Fact]
	public async Task UnlockAsync_Pbkdf2Header_Unlocks()
	{
		await using CoreTestContext context = new();
		byte[] dataKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
		VaultKdfParameters kdf = new()
		{
			Algorithm = VaultKdfParameters.Pbkdf2Sha256,
			Iterations = 1_000,
			Salt = new byte[VaultKdfParameters.SaltSize],
		};
		Guid vaultId = Guid.NewGuid();
		using (PinnedBytes passwordKey = await VaultKeyDerivation.DeriveAsync(CoreTestContext.MasterPassword, kdf, CancellationToken.None))
		{
			VaultHeader header = new()
			{
				Format = VaultHeader.CurrentFormat,
				VaultId = vaultId,
				Kdf = kdf,
				MasterKey = VaultCrypto.WrapKey(passwordKey.Span, dataKey, vaultId),
				KeyCheck = VaultCrypto.ComputeKeyCheck(dataKey),
			};
			await File.WriteAllBytesAsync(context.HeaderPath, JsonSerializer.SerializeToUtf8Bytes(header, MokatermJson.Document), Ct);
		}

		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		Assert.True((await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Succeeded);
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync("wrong", Ct)).Status);
	}

	[Fact]
	public async Task UnlockAsync_RepeatedFailures_AreThrottled()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope creator = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		for (int i = 0; i <= UnlockThrottle.FreeFailures; i++)
		{
			Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync("wrong", Ct)).Status);
		}

		UnlockResult throttled = await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct);
		Assert.Equal(UnlockStatus.Throttled, throttled.Status);
		Assert.Equal(TimeSpan.FromSeconds(1), throttled.RetryAfter);

		context.Time.Advance(TimeSpan.FromSeconds(1));
		Assert.True((await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Succeeded);

		vault.Lock();
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync("wrong", Ct)).Status);
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync("wrong", Ct)).Status);
	}

	[Fact]
	public async Task UnlockAsync_GuessesFromParallelScopes_AreCountedBeforeTheNextOneStarts()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope creator = await context.CreateVaultScopeAsync();
		List<AsyncServiceScope> tabs = [];
		try
		{
			List<IVault> vaults = [];
			for (int i = 0; i < 3; i++)
			{
				AsyncServiceScope tab = context.CreateScope();
				tabs.Add(tab);
				IVault vault = tab.ServiceProvider.GetRequiredService<IVault>();
				await vault.LoadAsync(Ct);
				vaults.Add(vault);
			}

			for (int i = 0; i < UnlockThrottle.FreeFailures; i++)
			{
				Assert.Equal(UnlockStatus.InvalidPassword, (await vaults[0].UnlockAsync("wrong", Ct)).Status);
			}

			// Three browser tabs guessing at once must not each get a guess in before the first failure is counted.
			UnlockResult[] results = await Task.WhenAll(vaults.Select(vault => vault.UnlockAsync("wrong", Ct)));

			Assert.Single(results, result => result.Status == UnlockStatus.InvalidPassword);
			Assert.Equal(2, results.Count(result => result.Status == UnlockStatus.Throttled));
		}
		finally
		{
			foreach (AsyncServiceScope tab in tabs)
			{
				await tab.DisposeAsync();
			}
		}
	}

	[Fact]
	public async Task Lock_WipesKeyAndBlocksEncryptedData()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		byte[] keyBuffer = GetKeyBuffer(vault);
		Assert.Contains(keyBuffer, value => value != 0);
		List<VaultStatus> statuses = [];
		vault.StatusChanged += statuses.Add;

		vault.Lock();

		Assert.All(keyBuffer, value => Assert.Equal(0, value));
		Assert.Equal(VaultStatus.Locked, vault.Status);
		Assert.Equal(LockReason.User, vault.LastLockReason);
		Assert.Equal(VaultStatus.Locked, Assert.Single(statuses));
		IVaultDataStore store = scope.ServiceProvider.GetRequiredService<IVaultDataStore>();
		await Assert.ThrowsAsync<VaultLockedException>(() => store.WriteAsync(DocumentName, new Note("x"), Ct));
		await Assert.ThrowsAsync<VaultLockedException>(async () => await store.ReadAsync<Note>(DocumentName, Ct));

		vault.Lock();
		Assert.Single(statuses);
	}

	[Fact]
	public async Task ChangeMasterPasswordAsync_RewrapsTheSameDataKey()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		IVaultDataStore store = scope.ServiceProvider.GetRequiredService<IVaultDataStore>();
		await store.WriteAsync(DocumentName, new Note("survives"), Ct);
		string saltBefore = (string)ReadHeader(context)["kdf"]!["salt"]!;

		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.ChangeMasterPasswordAsync("wrong current", "new password", Ct)).Status);
		Assert.True((await vault.ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "new password", Ct)).Succeeded);

		Assert.NotEqual(saltBefore, (string)ReadHeader(context)["kdf"]!["salt"]!);
		vault.Lock();
		Assert.Equal(UnlockStatus.InvalidPassword, (await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Status);
		Assert.True((await vault.UnlockAsync("new password", Ct)).Succeeded);
		Assert.Equal("survives", (await store.ReadAsync<Note>(DocumentName, Ct))?.Text);
	}

	// The dialog used to get plain false here and told the user the right password was wrong.
	[Fact]
	public async Task ChangeMasterPasswordAsync_WhileGuessesAreThrottled_SaysSoInsteadOfCallingThePasswordWrong()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		for (int i = 0; i <= UnlockThrottle.FreeFailures; i++)
		{
			Assert.Equal(UnlockStatus.InvalidPassword, (await vault.ChangeMasterPasswordAsync("wrong current", "new password", Ct)).Status);
		}

		UnlockResult throttled = await vault.ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "new password", Ct);

		Assert.Equal(UnlockStatus.Throttled, throttled.Status);
		Assert.Equal(TimeSpan.FromSeconds(1), throttled.RetryAfter);
		context.Time.Advance(TimeSpan.FromSeconds(1));
		Assert.True((await vault.ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "new password", Ct)).Succeeded);
	}

	[Fact]
	public async Task ChangeMasterPasswordAsync_WhileLocked_Throws()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		vault.Lock();

		await Assert.ThrowsAsync<VaultLockedException>(() => vault.ChangeMasterPasswordAsync(CoreTestContext.MasterPassword, "new password", Ct));
	}

	[Fact]
	public async Task DeviceUnlock_WithoutProtector_IsUnavailable()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();

		Assert.False(vault.IsDeviceUnlockAvailable);
		await Assert.ThrowsAsync<InvalidOperationException>(() => vault.SetDeviceUnlockAsync(true, Ct));
		vault.Lock();
		Assert.Equal(UnlockStatus.DeviceUnlockUnavailable, (await vault.UnlockWithDeviceAsync(Ct)).Status);
	}

	[Fact]
	public async Task DeviceUnlock_WithProtector_UnlocksWithoutPassword()
	{
		FakeDeviceKeyProtector protector = new();
		await using CoreTestContext context = new(services => services.AddSingleton<IDeviceKeyProtector>(protector));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await scope.ServiceProvider.GetRequiredService<IVaultDataStore>().WriteAsync(DocumentName, new Note("device"), Ct);
		Assert.True(vault.IsDeviceUnlockAvailable);
		Assert.False(vault.IsDeviceUnlockEnabled);

		await vault.SetDeviceUnlockAsync(true, Ct);

		Assert.True(vault.IsDeviceUnlockEnabled);
		Assert.Equal(1, protector.ProtectCalls);
		Assert.NotNull(ReadHeader(context)["device"]?["protectedKey"]);

		await using AsyncServiceScope other = context.CreateScope();
		IVault otherVault = other.ServiceProvider.GetRequiredService<IVault>();
		await otherVault.LoadAsync(Ct);
		Assert.True(otherVault.IsDeviceUnlockEnabled);
		Assert.Equal(UnlockResult.Success, await otherVault.UnlockWithDeviceAsync(Ct));
		Assert.Equal("device", (await other.ServiceProvider.GetRequiredService<IVaultDataStore>().ReadAsync<Note>(DocumentName, Ct))?.Text);
	}

	[Fact]
	public async Task DeviceUnlock_ProtectorFailureOrWrongKey_IsUnavailable()
	{
		FakeDeviceKeyProtector protector = new();
		await using CoreTestContext context = new(services => services.AddSingleton<IDeviceKeyProtector>(protector));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.SetDeviceUnlockAsync(true, Ct);
		vault.Lock();

		protector.FailUnprotect = true;
		Assert.Equal(UnlockStatus.DeviceUnlockUnavailable, (await vault.UnlockWithDeviceAsync(Ct)).Status);

		protector.FailUnprotect = false;
		protector.CorruptUnprotect = true;
		Assert.Equal(UnlockStatus.DeviceUnlockUnavailable, (await vault.UnlockWithDeviceAsync(Ct)).Status);
		Assert.Equal(VaultStatus.Locked, vault.Status);
	}

	[Fact]
	public async Task DeviceUnlock_AfterDisabling_IsUnavailable()
	{
		FakeDeviceKeyProtector protector = new();
		await using CoreTestContext context = new(services => services.AddSingleton<IDeviceKeyProtector>(protector));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.SetDeviceUnlockAsync(true, Ct);

		await vault.SetDeviceUnlockAsync(false, Ct);
		vault.Lock();

		Assert.False(vault.IsDeviceUnlockEnabled);
		Assert.Null(ReadHeader(context)["device"]);
		Assert.Equal(UnlockStatus.DeviceUnlockUnavailable, (await vault.UnlockWithDeviceAsync(Ct)).Status);
		await Assert.ThrowsAsync<VaultLockedException>(() => vault.SetDeviceUnlockAsync(true, Ct));
	}

	[Fact]
	public async Task ResetAsync_DeletesVaultKeepsSettingsAndMovesOtherScopes()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope first = await context.CreateVaultScopeAsync();
		await using AsyncServiceScope second = await context.UnlockedScopeAsync();
		IVault firstVault = first.ServiceProvider.GetRequiredService<IVault>();
		IVault secondVault = second.ServiceProvider.GetRequiredService<IVault>();
		await first.ServiceProvider.GetRequiredService<IVaultDataStore>().WriteAsync(DocumentName, new Note("gone"), Ct);
		await context.Services.GetRequiredService<ISettingsService>().UpdateAsync<AppearanceSettings>(settings => settings with { FontScale = 1.25 }, Ct);
		await context.Services.GetRequiredService<ISettingsService>().FlushAsync(Ct);
		List<VaultStatus> firstStatuses = [];
		List<VaultStatus> secondStatuses = [];
		firstVault.StatusChanged += firstStatuses.Add;
		secondVault.StatusChanged += secondStatuses.Add;
		byte[] secondKey = GetKeyBuffer(secondVault);

		await firstVault.ResetAsync(Ct);

		Assert.False(File.Exists(context.HeaderPath));
		Assert.False(Directory.Exists(context.VaultDirectory));
		Assert.True(File.Exists(Path.Combine(context.DataDirectory, "settings.json")));
		Assert.Equal(VaultStatus.Uninitialized, firstVault.Status);
		Assert.Equal(VaultStatus.Uninitialized, secondVault.Status);
		Assert.Equal(LockReason.Reset, firstVault.LastLockReason);
		Assert.Equal(LockReason.Reset, secondVault.LastLockReason);
		Assert.Collection(firstStatuses, status => Assert.Equal(VaultStatus.Locked, status), status => Assert.Equal(VaultStatus.Uninitialized, status));
		Assert.Collection(secondStatuses, status => Assert.Equal(VaultStatus.Locked, status), status => Assert.Equal(VaultStatus.Uninitialized, status));
		Assert.All(secondKey, value => Assert.Equal(0, value));

		await secondVault.CreateAsync("fresh start", Ct);
		Assert.Equal(VaultStatus.Locked, firstVault.Status);
		Assert.Equal(UnlockStatus.InvalidPassword, (await firstVault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Status);
		Assert.True((await firstVault.UnlockAsync("fresh start", Ct)).Succeeded);
		Assert.Null(await first.ServiceProvider.GetRequiredService<IVaultDataStore>().ReadAsync<Note>(DocumentName, Ct));
	}

	[Fact]
	public async Task ResetAsync_OnTheWebHostWhileLocked_IsRefusedUntilUnlocked()
	{
		// Anyone who reaches a web host sees its lock screen; a reset from there would wipe every saved host.
		string directory = TempFolder.NewPath(CoreTestContext.TempFolderPrefix);
		await using CoreTestContext context = new(
			services => services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(directory) { Kind = HostKind.Web }),
			directory);
		await using (AsyncServiceScope creator = await context.CreateVaultScopeAsync())
		{
			await creator.ServiceProvider.GetRequiredService<IVaultDataStore>().WriteAsync(DocumentName, new Note("kept"), Ct);
		}

		await using AsyncServiceScope scope = context.CreateScope();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();
		await vault.LoadAsync(Ct);

		InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => vault.ResetAsync(Ct));

		Assert.Contains("data directory", refused.Message, StringComparison.Ordinal);
		Assert.True(File.Exists(context.HeaderPath));
		Assert.Equal(VaultStatus.Locked, vault.Status);
		Assert.True((await vault.UnlockAsync(CoreTestContext.MasterPassword, Ct)).Succeeded);
		Assert.Equal("kept", (await scope.ServiceProvider.GetRequiredService<IVaultDataStore>().ReadAsync<Note>(DocumentName, Ct))?.Text);

		await vault.ResetAsync(Ct);

		Assert.False(File.Exists(context.HeaderPath));
		Assert.Equal(VaultStatus.Uninitialized, vault.Status);
	}

	[Fact]
	public async Task CreateAsync_InOtherScope_MovesUninitializedScopeToLocked()
	{
		await using CoreTestContext context = new();
		await using AsyncServiceScope waiting = context.CreateScope();
		IVault waitingVault = waiting.ServiceProvider.GetRequiredService<IVault>();
		await waitingVault.LoadAsync(Ct);
		List<VaultStatus> statuses = [];
		waitingVault.StatusChanged += statuses.Add;

		await using AsyncServiceScope creator = await context.CreateVaultScopeAsync();

		Assert.Equal(VaultStatus.Locked, waitingVault.Status);
		Assert.Equal(VaultStatus.Locked, Assert.Single(statuses));
	}

	[Fact]
	public async Task IdleTimer_LocksAfterConfiguredMinutesWithoutActivity()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();

		context.Time.Advance(TimeSpan.FromMinutes(14));
		Assert.Equal(VaultStatus.Unlocked, vault.Status);

		vault.ReportActivity();
		context.Time.Advance(TimeSpan.FromMinutes(14));
		Assert.Equal(VaultStatus.Unlocked, vault.Status);

		context.Time.Advance(TimeSpan.FromMinutes(1));
		Assert.Equal(VaultStatus.Locked, vault.Status);
		Assert.Equal(LockReason.Idle, vault.LastLockReason);
	}

	[Fact]
	public async Task IdleTimer_ReadsSettingEachTickAndZeroDisablesIt()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));
		ISettingsService settings = context.Services.GetRequiredService<ISettingsService>();
		await using AsyncServiceScope scope = await context.CreateVaultScopeAsync();
		IVault vault = scope.ServiceProvider.GetRequiredService<IVault>();

		await settings.UpdateAsync<SecuritySettings>(security => security with { AutoLockMinutes = 0 }, Ct);
		context.Time.Advance(TimeSpan.FromHours(3));
		Assert.Equal(VaultStatus.Unlocked, vault.Status);

		await settings.UpdateAsync<SecuritySettings>(security => security with { AutoLockMinutes = 1 }, Ct);
		context.Time.Advance(TimeSpan.FromSeconds(15));
		Assert.Equal(VaultStatus.Locked, vault.Status);
	}

	private static JsonObject ReadHeader(CoreTestContext context) =>
		JsonNode.Parse(File.ReadAllText(context.HeaderPath, Encoding.UTF8))!.AsObject();

	private static byte[] GetKeyBuffer(IVault vault)
	{
		FieldInfo field = typeof(Vault).GetField("_dataKey", BindingFlags.NonPublic | BindingFlags.Instance)
			?? throw new InvalidOperationException("Vault no longer keeps its key in _dataKey.");
		PinnedBytes key = (PinnedBytes)(field.GetValue(vault) ?? throw new InvalidOperationException("The vault holds no key."));
		return key.Array;
	}

	public sealed record Note(string Text);
}
