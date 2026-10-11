using Mokaterm.Modules.Local.Containers;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>Finding docker.exe and kubectl.exe on a computer described by the test: PATH first, then the install folders.</summary>
public sealed class ContainerCliTests
{
	private const string LocalAppData = @"C:\Users\ada\AppData\Local";

	private static readonly string DockerDesktop = Path.Combine(TestMachine.ProgramFiles, "Docker", "Docker", "resources", "bin");

	private static readonly string DockerDesktopForOneUser = Path.Combine(LocalAppData, "Programs", "DockerDesktop", "resources", "bin");

	private static readonly string RancherDesktop = Path.Combine(TestMachine.ProgramFiles, "Rancher Desktop", "resources", "resources", "win32", "bin");

	private static readonly string WinGetLinks = Path.Combine(LocalAppData, "Microsoft", "WinGet", "Links");

	[Fact]
	public void Nothing_Installed_FindsNothing()
	{
		TestMachine machine = Machine();

		Assert.Null(ContainerCli.FindDocker(machine.Build()));
		Assert.Null(ContainerCli.FindKubectl(machine.Build()));
	}

	[Fact]
	public void OnPath_WinsOverEveryInstallFolder()
	{
		TestMachine machine = Machine();
		machine.Variables["PATH"] = @"C:\Windows\System32;C:\Tools";
		machine.Files.Add(@"C:\Tools\docker.exe");
		machine.Files.Add(Path.Combine(DockerDesktop, "docker.exe"));

		Assert.Equal(@"C:\Tools\docker.exe", ContainerCli.FindDocker(machine.Build()));
	}

	[Fact]
	public void DockerDesktop_ForAllUsers_IsFoundOffPath()
	{
		TestMachine machine = Machine();
		machine.Files.Add(Path.Combine(DockerDesktop, "docker.exe"));
		machine.Files.Add(Path.Combine(DockerDesktop, "kubectl.exe"));

		Assert.Equal(Path.Combine(DockerDesktop, "docker.exe"), ContainerCli.FindDocker(machine.Build()));
		Assert.Equal(Path.Combine(DockerDesktop, "kubectl.exe"), ContainerCli.FindKubectl(machine.Build()));
	}

	[Fact]
	public void DockerDesktop_ForOneUser_IsFoundUnderLocalAppData()
	{
		TestMachine machine = Machine();
		machine.Files.Add(Path.Combine(DockerDesktopForOneUser, "docker.exe"));

		Assert.Equal(Path.Combine(DockerDesktopForOneUser, "docker.exe"), ContainerCli.FindDocker(machine.Build()));
	}

	[Fact]
	public void RancherDesktop_AndWinGet_AreLookedInToo()
	{
		TestMachine machine = Machine();
		machine.Files.Add(Path.Combine(RancherDesktop, "docker.exe"));
		machine.Files.Add(Path.Combine(WinGetLinks, "kubectl.exe"));

		Assert.Equal(Path.Combine(RancherDesktop, "docker.exe"), ContainerCli.FindDocker(machine.Build()));
		Assert.Equal(Path.Combine(WinGetLinks, "kubectl.exe"), ContainerCli.FindKubectl(machine.Build()));
	}

	[Fact]
	public void ARelativePathEntry_IsNeverSearched()
	{
		TestMachine machine = Machine();
		machine.Variables["PATH"] = @"tools;C:\Windows";
		machine.Files.Add(@"tools\docker.exe");

		Assert.Null(ContainerCli.FindDocker(machine.Build()));
	}

	[Fact]
	public void WithoutTheFolderVariables_OnlyPathIsSearched()
	{
		TestMachine machine = new();
		machine.Variables.Remove("ProgramFiles");
		machine.Files.Add(Path.Combine(DockerDesktop, "docker.exe"));

		Assert.Empty(ContainerCli.InstallFolders(machine.Build()));
		Assert.Null(ContainerCli.FindDocker(machine.Build()));
	}

	private static TestMachine Machine()
	{
		TestMachine machine = new();
		machine.Variables["LOCALAPPDATA"] = LocalAppData;
		return machine;
	}
}
