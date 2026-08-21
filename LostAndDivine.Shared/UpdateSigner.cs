using System.Security.Cryptography;
using System.Text;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Shared;

/// <summary>
/// Проверка подписи манифеста обновлений клиента. Подпись ставится при сборке
/// приватным ключом на машине разработчика; клиент проверяет её вшитым публичным
/// ключом (SigningKeys). Подписывается детерминированный «отпечаток» манифеста
/// (версия + список файлов), поэтому неважно, в каком виде сервер отдал JSON.
/// </summary>
public static class UpdateSigner
{
    /// <summary>Детерминированный отпечаток манифеста, по которому ставится подпись.</summary>
    public static byte[] BuildSignInput(UpdateInfo info)
    {
        var sb = new StringBuilder();
        sb.Append(info.Version ?? "");
        sb.Append('\n');
        foreach (var f in (info.Files ?? new List<UpdateFileEntry>()).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(f.Path ?? "");
            sb.Append('|');
            sb.Append(f.Sha256 ?? "");
            sb.Append('|');
            sb.Append(f.Size);
            sb.Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Проверяет подпись манифеста заданным публичным ключом (PEM).</summary>
    public static bool Verify(UpdateInfo info, string publicKeyPem)
    {
        if (info?.Signature == null) return false;
        byte[] sig;
        try { sig = Convert.FromBase64String(info.Signature); }
        catch { return false; }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            byte[] data = BuildSignInput(info);
            return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
