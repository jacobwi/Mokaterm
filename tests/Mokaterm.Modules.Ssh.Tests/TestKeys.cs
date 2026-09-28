namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Throwaway keys generated for these tests with OpenSSH 10.3 <c>ssh-keygen</c>. They protect nothing. Expected
/// fingerprints and public keys are what <c>ssh-keygen -l</c> and the <c>.pub</c> files reported, so they check this module
/// against OpenSSH rather than against itself.
/// </summary>
internal static class TestKeys
{
	public const string EncryptedEd25519Passphrase = "correct horse battery";

	public const string EncryptedRsaPemPassphrase = "staple 42";

	public const string Ed25519 = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
		QyNTUxOQAAACAI5Vo6VCg9Ixi+jUmzWNHzjS0VplQan8rUQ27VXoLvqwAAAJAjKYSwIymE
		sAAAAAtzc2gtZWQyNTUxOQAAACAI5Vo6VCg9Ixi+jUmzWNHzjS0VplQan8rUQ27VXoLvqw
		AAAEB+78+feZrPSByn4o2UJTs3UmYlePCTVaMTgcbncGsEeAjlWjpUKD0jGL6NSbNY0fON
		LRWmVBqfytRDbtVegu+rAAAADW1va2F0ZXJtLXRlc3Q=
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string Ed25519Fingerprint = "SHA256:+DqLELjVSMAgxkyyH2Fn+gj01rnETSk5J0E6m4Ty5rI";

	public const string Ed25519PublicKey = "AAAAC3NzaC1lZDI1NTE5AAAAIAjlWjpUKD0jGL6NSbNY0fONLRWmVBqfytRDbtVegu+r";

	public const string EncryptedEd25519 = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAACmFlczI1Ni1jdHIAAAAGYmNyeXB0AAAAGAAAABA0kMW439
		nDj6xjiUpedh95AAAACAAAAAEAAAAzAAAAC3NzaC1lZDI1NTE5AAAAIKQTzefUXPJipGJ3
		jMy4v8DK3QJMNnIVAg+y4bNKl3k/AAAAkBP3/7iNqtmsFiV9xbJhqDd3J+kIw5YuWUQUOH
		4skeMJfWkmMN9bLrT8Wk2wd9OY4U1nksPFUSgPiZCB0p6VbMSM/AnVUBSrzcYSQVTqHHeJ
		KGJe975a6E84mjAIsDfUxFuMXiJuJvHA96qTaEWjt0410gzdyZSNi5jqbRj5g4BLX31I5R
		qYEg2wT0xZKyMD/Q==
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string EncryptedEd25519Fingerprint = "SHA256:G9LLVvpWzeD8FJPwppJqoxYFcau4soMsdts2QlHh8ZY";

	public const string EcdsaP256 = """
		-----BEGIN OPENSSH PRIVATE KEY-----
		b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAaAAAABNlY2RzYS
		1zaGEyLW5pc3RwMjU2AAAACG5pc3RwMjU2AAAAQQR7g2xnS+D2eC7I9JceCFTqZ4uZJm0Q
		DhiRKf00A5mRf9XP9vCJy9XdFf64Zo4abVkudBfO4qYRR2ZwGzq4O0/5AAAAqFBgWJNQYF
		iTAAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBHuDbGdL4PZ4Lsj0
		lx4IVOpni5kmbRAOGJEp/TQDmZF/1c/28InL1d0V/rhmjhptWS50F87iphFHZnAbOrg7T/
		kAAAAhANYFKYHUeQ1uQgi091g2Qx472vgnwLKDnsDIVGjtO1GAAAAADW1va2F0ZXJtLXRl
		c3QBAg==
		-----END OPENSSH PRIVATE KEY-----
		""";

	public const string EcdsaP256Fingerprint = "SHA256:coS/GFZ/ZCj+scSpzHGYKnisttpRiGD1f/Pl1CK5IiA";

	public const string EncryptedRsaPem = """
		-----BEGIN RSA PRIVATE KEY-----
		Proc-Type: 4,ENCRYPTED
		DEK-Info: AES-128-CBC,5E8F1E1381B926CFF45D0D5535B908E0

		CBDqqf2DvlAXX65BGwy3nLpp+vB10QwIzSVfAxudgT8Mx3sHnyGfabW44YLpm4uc
		lU+ShhLxYGYBk7mXKjM/+wjeNRvJWPg2HgLMl5uMKMt+LUn7+VXTm5chZ3mhFN/F
		jYfqDREEubAopiLxyqBVXv+PHsuQ5WAT0w4D9kAm2Kf0pHPPMQIKwkhwjI+mrcdT
		gz/AqYbHybQ+LThS0mR78eZGQ2qIBqe9N5Bnm/gg3l1lWrCJ8O3hZFDy4DhYLXn4
		V2Ey1ZuaRZ52y1WRuHPU/LzMwIDtCbNQoZf4vz3Ydd2bg1NDPlQRyTQrHz7nOY3l
		BU5X32hImA/y+vlo9XxytqH+rzh+LNg8tO3g+EO03+HoGlO6tkHhNj7700tQFbTG
		59hVEsD3RXAl4Jvh7CW3AtBnvcdgiJgySiXoIFlxA4KRz14wREvVmdbRjJLopZsZ
		9oh47MXbRciwcoSbzyYIH/0Wi+vGQCDOuJ0IA5zJeG55GgD86dcvyI5owbFju43C
		z3aWxzkkpya1DEOL9Ej9UXLg/twQNfRMfQA3p5jX85mfyoJlQW5wlrZYYasGVDoL
		u3D798ms++3lgYRI6c0b3U5KFQN6s6iREpPgfFeJTiUni5Ndv2T8xC/PM5G9WajN
		9lSfzoF3Z80a/b7rBOx8CmHfanEqKzw+f7gaanZy2YBOjzkYsptH9a25+Fbbhrui
		0xKMpW5if+DwsgaO+D+GT74yaNwmV6EY158Bq9flBtPyMzKEm1z8UnGdSQbLpldN
		Y1tVWbl2rbFSay+IdBIhSxtSEoaBdlay58QDEz3VDk9RdczTR55+LlLaL3tlCeIm
		OAZ2Uaf9kiXA5k6zK+lMGsI8fQnvV3/FYe+qaQeARF90Sku0hWlmNCRnuiGkIJQb
		TRWyIMpUrFzR2bkZiH4t4qv0a/dg5H3guWOYT4vwIEU5ey2jAqIu6zkksrIQDKLh
		ZsE5UEEqlqnk/j69PYnXAGThvAMnJcwS7tohBKJ0L1/onX94i9Hg9DIk7EcSOHY5
		MMuKBEzx3YScIhs+f6WVasyt+TYgmkJPXGe7SSqbdgHVvyWfZAVR+roWk2P6OgOc
		1t1O3c0dWyrRDnvC8EiHiRRqsUWGMjAuRpT3WWR8EFdg43H5/KjeqRCEwqo7aYvS
		zHqFCVKddELVCm4qIqqTTo/4KH+SdzVvXXG/jG+9mj9aQnBliQ8jpGGAIBDUznRQ
		RlQkRi/sSm99RVtxCw5iQNuaLCCpOqTosfFuYm6gnElCvtHGGqqYtpoxpljLvFVg
		W5oLE7hEP2rZhP+B7ySLNQKPaMd52v5b2oK9Cs6YcbnuOTnQJXHtIIy5SX0pkwj5
		oX/wTCO5QP+9hS6KUBJB7/bJo8sG99NC1JQrRn/JXMAswwKBfhRV04X1AVKNYYXL
		CHhDaKBuTJ7u5eCdasJwOYZ3jqkdHDvVahMiuVKmg+Qcfvmslm14QyYDZdDBM8z4
		6kujmIB3m6lTBJGg12qlHlx1hUNtIkb23+PSrFcHEkVybK8i+KdmlSQCJ0QlnbEw
		RS129LyooZQ6u8GENo9m8t+xvsjc1jayk+oSLXTfvjC8eZJzbhwNLw6JD1m4WRtQ
		-----END RSA PRIVATE KEY-----
		""";

	public const string EncryptedRsaPemFingerprint = "SHA256:FNH1ucGNCp5vGH4cz7YeIEiTELXAeoIVoWf7Uxz/q1U";

	public const string RsaPkcs8 = """
		-----BEGIN PRIVATE KEY-----
		MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQCE0ZdP7rupFlMe
		PyuYM3yVsi1tabjsVzXwJx27pHS7iPSShm/UonN4Kx/g39FK4o9FxD+FWVvGkULs
		P8TrFlN7FG/4I6KVgu9Db/CtaQM90qxBgkr2tEyY2WWa+rBPTHWibv66l5dtxc0z
		U72w23w0U6ATP8BtFE4BhtWTkMH9h32AN1aM9UvX1CyI8Mza6ZqTIQz64jtjHdYl
		UclrMDMt8eZrKx7dfX2uxFbFEdthDu6K/ewiTLpKy78Z+Cr9epF5/wXvb103Zlfj
		Coy5F0JjaFJVTHbLdbUbKf6pE0Bm15ZCK5T+Mhsf/Wd7MAO216r1h8P5Llk4DdqM
		rD3tAH0XAgMBAAECggEAEGKe7SCBjDGR3Xhjk2o+o2UQz5+sTZPQtqjMtTproDsc
		GPt2zl+gKSIA08go1LkwfaXhRD0q4ktHW0ferjXZ5KQ3z//tbc5yX/puUaZT3Quh
		bo95WggSLwUa0My+dXn0RS62RCSxbLxrPTrGewwIvZfXdqh1v78yhGOy83p3RCSG
		arw/nYYAbY+XujeL47wicJYJ8iYbAEheyeaHteuMHXhCicq5lWkb3Hh3JY+1jrvN
		zW0n9B6pwIf0ZcIhjHnnDywkWMd14PxdcPykJ8O/bZ/V9uFiTFcflX9IBW1OMb6D
		yHl3+n2+mwI/kSttotl7YYckweZwIkm+7x7vsdjsAQKBgQC5XguFdAbrOTsMOuOC
		MsU1FTyOTJty//ZTZLl/WSSMZH6BNU7iVSpI2DDn+fMvJoNzq4r/TmhmpBnRQbIu
		EzWZUnQJ9c2PzxQbZnVcyafyrMbO9tIdtesWmk8/lcPP866xI1aSsPB50oD3ftjm
		PvNgT8YUm5PTOjC6Rr3hrcKnFwKBgQC3bZq5IqHhOBo9ufPk4ZRCIeUMRjp1/IVb
		/M8UYvASSMIUPre/hTtSziSfVTWPz/xHwrMgdlaSr+sdol2BF2Au3eMWaq1Wt+JS
		3JWH8npCyGrf9ltoMkdWIeZRn6oivvCctMhkfeZ/z3g9V6h/JEO34vIW75EF8+zb
		F6y6Q1aaAQKBgFNFpUVZXzZSam1PEx9NMXxsQtzmGzspM345FH/aCuoqxw3CZeKV
		Qb6hqC6+AXogAhN3b6TLk2jwtUxlB+dc58ot2UUUMDk3XKAGghih2pnQ9irJhJCm
		RarMvWciH2oO3V5kMqMJa64+W4NTb5rXkrukeqaNUXYlgPhdiTWVuokXAoGAX4K9
		1vrWidxZpZyHB74CIfFeP+btl/QWSNC5zya5VPv3uuzxZtNsEXDvltuamTi2Z3NV
		LGkSKS1a4sJhp89RMPYuwcRoX8g+G+FqJzC2QsHzDI1OCVJs3MeoEwZtY8xCo4Zq
		9hCsjg6s0Fwti46JiF2uqwyxWgUqZubL9O0NUgECgYARivYq2J0YJkZLn6i/6pau
		5Nd/eiHI/YNHaGE+PN4u7YA+G4rIxMdUrOslnw6vkfpShAq7POg43C52bqNyJbOo
		jsYp9VT3pABWpGZ1zgkRlrVgkrUNcQxLj9S5vcPI7ZFsF/MteBwCcoidMaLFaa64
		dOAGyNaxED+K40i03MEzcA==
		-----END PRIVATE KEY-----
		""";

	public const string RsaPkcs8Fingerprint = "SHA256:LIflXhKkVv0/difBTRQ4BNwK+KFJ+MWe+/uIvTQe2EM";
}
