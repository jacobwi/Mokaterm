using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mokaterm.Modules.Rdp.Protocol;

[SupportedOSPlatform("windows")]
internal static class RdpDomainLocator
{
	private const uint IsFlatName = 0x0001_0000;

	private const uint ReturnDnsName = 0x4000_0000;

	public static string? DnsNameOf(string shortName)
	{
		if (DsGetDcName(null, shortName, 0, null, IsFlatName | ReturnDnsName, out nint info) != 0 || info == 0)
		{
			return null;
		}

		try
		{
			// DOMAIN_CONTROLLER_INFOW: two pointers, a ULONG and a GUID come before DomainName, which is pointer aligned.
			int offset = Align((2 * IntPtr.Size) + sizeof(uint) + 16, IntPtr.Size);
			string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, offset));
			return string.IsNullOrWhiteSpace(name) ? null : name.TrimEnd('.');
		}
		finally
		{
			_ = NetApiBufferFree(info);
		}
	}

	private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

	[DllImport("netapi32.dll", EntryPoint = "DsGetDcNameW", ExactSpelling = true, CharSet = CharSet.Unicode)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int DsGetDcName(string? computerName, string domainName, nint domainGuid, string? siteName, uint flags, out nint domainControllerInfo);

	[DllImport("netapi32.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int NetApiBufferFree(nint buffer);
}
