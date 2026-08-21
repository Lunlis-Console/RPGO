using LostAndDivine.Shared;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Tests;

public class UpdateSignerTests
{
    private static UpdateInfo SampleManifest()
    {
        return new UpdateInfo
        {
            Version = "0.1.42",
            Files = new List<UpdateFileEntry>
            {
                new() { Path = "Game.exe", Size = 1234567, Sha256 = "aaaabbbb" },
                new() { Path = "data/config.json", Size = 42, Sha256 = "deadbeef" },
                new() { Path = "Data/Assets.bin", Size = 999, Sha256 = "feedface" },
            }
        };
    }

    [Fact]
    public void Verify_Accepts_ValidSignature()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string publicPem = rsa.ExportSubjectPublicKeyInfoPem();

        var info = SampleManifest();
        byte[] data = UpdateSigner.BuildSignInput(info);
        string sig = Convert.ToBase64String(rsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
        info.Signature = sig;

        Assert.True(UpdateSigner.Verify(info, publicPem));
    }

    [Fact]
    public void Verify_Rejects_TamperedManifest()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string publicPem = rsa.ExportSubjectPublicKeyInfoPem();

        var info = SampleManifest();
        byte[] data = UpdateSigner.BuildSignInput(info);
        string sig = Convert.ToBase64String(rsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
        info.Signature = sig;

        // Меняем версию на лету — подпись должна перестать проходить
        info.Version = "0.1.43";
        Assert.False(UpdateSigner.Verify(info, publicPem));
    }

    [Fact]
    public void Verify_Rejects_MissingSignature()
    {
        var info = SampleManifest();
        Assert.False(UpdateSigner.Verify(info, SigningKeys.PublicKeyPem));
    }
}
