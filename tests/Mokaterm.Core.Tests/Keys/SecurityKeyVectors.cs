namespace Mokaterm.Core.Tests.Keys;

/// <summary>
/// Security key files from OpenSSH's own test suite (regress/unittests/sshkey/testdata), written by <c>ssh-keygen</c>
/// through its software middleware, whose key handle is the private key itself. The public lines and fingerprints are
/// what <c>ssh-keygen -y</c> and <c>-l</c> print for them, so these check the reader against OpenSSH, not against our
/// own writer.
/// </summary>
internal static class SecurityKeyVectors
{
	/// <summary>The passphrase of the encrypted pair (regress/unittests/sshkey/testdata/pw).</summary>
	public const string Passphrase = "mekmitasdigoat";

	public const string EcdsaSk = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAfwAAACJzay1lY2
		RzYS1zaGEyLW5pc3RwMjU2QG9wZW5zc2guY29tAAAACG5pc3RwMjU2AAAAQQRnVT5Cji1D
		Ge2+q2X0vATh6LYnODV+DJrshJorr5GnipW29RfuaDXs0WB6XBej9dOLazVRDjQrtV19Qg
		O6cfkFAAAABHNzaDoAAAGQuPdnP7j3Zz8AAAAic2stZWNkc2Etc2hhMi1uaXN0cDI1NkBv
		cGVuc3NoLmNvbQAAAAhuaXN0cDI1NgAAAEEEZ1U+Qo4tQxntvqtl9LwE4ei2Jzg1fgya7I
		SaK6+Rp4qVtvUX7mg17NFgelwXo/XTi2s1UQ40K7VdfUIDunH5BQAAAARzc2g6AQAAAOMt
		LS0tLUJFR0lOIEVDIFBSSVZBVEUgS0VZLS0tLS0KTUhjQ0FRRUVJRURmVFB4YzA0alN5Zk
		Z5NlhoV1pTVlpzcnU5ZFlaSVpTOWhjeVFhcDlVT29Bb0dDQ3FHU000OQpBd0VIb1VRRFFn
		QUVaMVUrUW80dFF4bnR2cXRsOUx3RTRlaTJKemcxZmd5YTdJU2FLNitScDRxVnR2VVg3bW
		cxCjdORmdlbHdYby9YVGkyczFVUTQwSzdWZGZVSUR1bkg1QlE9PQotLS0tLUVORCBFQyBQ
		UklWQVRFIEtFWS0tLS0tCgAAAAAAAAAURUNEU0EtU0sgdGVzdCBrZXkgIzEBAgMEBQ==
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string EcdsaSkEncrypted = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAACmFlczI1Ni1jdHIAAAAGYmNyeXB0AAAAGAAAABB6vcJVx2
		cPc7yYRROup8VnAAAAEAAAAAEAAAB/AAAAInNrLWVjZHNhLXNoYTItbmlzdHAyNTZAb3Bl
		bnNzaC5jb20AAAAIbmlzdHAyNTYAAABBBGdVPkKOLUMZ7b6rZfS8BOHotic4NX4MmuyEmi
		uvkaeKlbb1F+5oNezRYHpcF6P104trNVEONCu1XX1CA7px+QUAAAAEc3NoOgAAAZBrvCxe
		xFz0bvzXwaPhrUHBeNCoZy/wNKDx0kxlxUPuA+lgOvy5l3lT3yxxd0qj5PQB+NTcuz8AAE
		1f7aSWQNZSifox3COsBGoHV9C8i+glcxiBKheAZD+EBnRGjG8kbcaLhuYDW/I39qNe8lHW
		YSDjmvsT55Hy0IAtVRAXizDoXKNdFPTZisC67WyOSJ3ED7Fy4bfT4ApbvhoFTwjikZBEhy
		LOad1sbJa4eT19TsskYfQdnJf8sjAmCMOZY4ZV0FiNW5XZOp8nIal1oyULPfzTAm6oaeFN
		0ImCSU3U8h4wUQ8q/3XvBWtTKycZaoou0AwPoP0QN95Ywte7FHezNPb/n8KD7k0S6h9XAX
		UcBeCe5NHyov/0ZzA2p737hzm3w+MXGOboTQMu8WFXeGh4m7QH2o8ZJdgBhM5JF17uii+Q
		ppGoPWHf33MXwB3wxWmKZ0ua0f9AVLkQ2DfFszUoBJE/kcHRd4kj4Q4FWXeMBN0GoH8gdE
		gRWIlxn2/FAOce/BFPzzdP87H0jwz7SdcuVO1L
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string EcdsaSkPublicKey =
		"sk-ecdsa-sha2-nistp256@openssh.com AAAAInNrLWVjZHNhLXNoYTItbmlzdHAyNTZAb3BlbnNzaC5jb20AAAAIbmlzdHAyNTYAAABBBGdVPkKOLUMZ7b6rZfS8BOHotic4NX4MmuyEmiuvkaeKlbb1F+5oNezRYHpcF6P104trNVEONCu1XX1CA7px+QUAAAAEc3NoOg== ECDSA-SK test key #1";

	public const string EcdsaSkFingerprint = "SHA256:Go7HO0CVPYG+BSDSk9ZUJBKGSrtBExp6obTa9iqzIUo";

	public const string Ed25519Sk = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAASgAAABpzay1zc2
		gtZWQyNTUxOUBvcGVuc3NoLmNvbQAAACAhaP5OS1PPOt7uumAvXlDtte9EHbqIT1EZEJ2y
		2v3XMwAAAARzc2g6AAAAuBocY6UaHGOlAAAAGnNrLXNzaC1lZDI1NTE5QG9wZW5zc2guY2
		9tAAAAICFo/k5LU8863u66YC9eUO2170QduohPURkQnbLa/dczAAAABHNzaDoBAAAAQJYq
		lGHhFoA25/q8X/rdTqDAb7dhqs4ehhd/w8x99CwiIWj+TktTzzre7rpgL15Q7bXvRB26iE
		9RGRCdstr91zMAAAAAAAAAFkVEMjU1MTktU0sgdGVzdCBrZXkgIzEBAgM=
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string Ed25519SkEncrypted = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAACmFlczI1Ni1jdHIAAAAGYmNyeXB0AAAAGAAAABDr5R9Yf/
		ucEh0Ns6c34tcIAAAAEAAAAAEAAABKAAAAGnNrLXNzaC1lZDI1NTE5QG9wZW5zc2guY29t
		AAAAICFo/k5LU8863u66YC9eUO2170QduohPURkQnbLa/dczAAAABHNzaDoAAADA2T6owx
		OSgKz4DvLnS3UJ/renbuew5mbkIWB1/y8xd3y5Usm08iUCAlKxep9dVRQvmyoTrc/7rHOM
		DkokNw+WgKambnlYT/9QfqViZ9iCBtbdmhLM6ksUCgQefvquRyXoJxlWstjXUll6Ru+ZbT
		H//Ss8C1bYtAiXR68OQ+rhDrvQxA9P8J1sGIlkuV3h8YXddSpyBW2Sn0LTHHBXYZo86cXZ
		G4Lnc8aGYm65eqdHgkfRmht3eS8DTdzEBfBNH5Ml
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string Ed25519SkPublicKey =
		"sk-ssh-ed25519@openssh.com AAAAGnNrLXNzaC1lZDI1NTE5QG9wZW5zc2guY29tAAAAICFo/k5LU8863u66YC9eUO2170QduohPURkQnbLa/dczAAAABHNzaDo= ED25519-SK test key #1";

	public const string Ed25519SkFingerprint = "SHA256:6WZVJ44bqhAWLVP4Ns0TDkoSQSsZo/h2K+mEvOaNFbw";
}
