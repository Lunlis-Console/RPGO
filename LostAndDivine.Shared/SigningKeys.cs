namespace LostAndDivine.Shared;

/// <summary>
/// Публичный ключ для проверки подписи манифеста обновлений клиента.
/// Приватный ключ НАМЕРЕННО НЕ лежит в репозитории — он только на машине сборки
/// (по умолчанию %LOCALAPPDATA%\LostAndDivine\sign_private.xml, на съёмном диске
/// с маркером LAD_KEYDRIVE.txt, или путь в LAD_SIGN_KEY_PATH).
/// </summary>
public static class SigningKeys
{
    public const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsHYvZF3cnJCoqvKuR8EX
bDDX1/i1ZzLIkhaZzV8nlLQmZbMm9S5K7f3a21upEKKyMPP5TVByvxVH2oMKrRdC
rwvTFwLV/z/iysOB3dToUB56uZnJ3DnfzefBNaICnAtcjy5zo60rjoRUIgE0NIAW
Ou7LqQixWJff2P0QrTy6ownItlVjsG+rwKamh6/dmgpRribgmVtufaAytdpRc/6t
Lxj2QLvNfCgWErMARK4Zt+fzbu4Zh9psTT1FsRujXMhbCEPXwfIsr6I81kgREdSp
jiykQJ7KD5jOzEkmqTK+lLAYEPhGS/4jdMFiUa1f04Dvx/9DPMhND5dwWa7j6ZTi
0QIDAQAB
-----END PUBLIC KEY-----";
}
