using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using PackEngine.Workspace;

namespace PackEngine.Editor.Android;

internal sealed class AndroidAiCredentials(string root) : IAiCredentialStore
{
    private static string Alias(string provider) => provider is "anthropic" or "openai" ? "PackEngine.AI." + provider : throw new ArgumentException("API 제공자가 아니야.");
    private string FileFor(string provider) { _ = Alias(provider); return Path.Combine(root, "Credentials", provider + ".bin"); }
    private static KeyStore Store() { var store = KeyStore.GetInstance("AndroidKeyStore")!; store.Load(null); return store; }
    private static IKey Key(string provider)
    {
        string alias = Alias(provider); using var store = Store();
        if (!store.ContainsAlias(alias))
        {
            using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            using var spec = new KeyGenParameterSpec.Builder(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)!.SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!.Build()!;
            generator.Init(spec); generator.GenerateKey();
        }
        return store.GetKey(alias, null)!;
    }
    public string Read(string provider)
    {
        string path = FileFor(provider); if (!File.Exists(path)) return "";
        byte[] bytes = File.ReadAllBytes(path); if (bytes.Length < 29) throw new InvalidDataException("저장된 AI 키를 다시 연결해줘.");
        using var key = Key(provider); using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128, bytes.Take(12).ToArray()); cipher.Init(CipherMode.DecryptMode, key, spec);
        return Encoding.UTF8.GetString(cipher.DoFinal(bytes.Skip(12).ToArray())!);
    }
    public void Write(string provider, string secret)
    {
        using var key = Key(provider); using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!; cipher.Init(CipherMode.EncryptMode, key);
        byte[] iv = cipher.GetIV()!; if (iv.Length != 12) throw new InvalidDataException("API 키 암호화를 준비하지 못했어.");
        byte[] encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(secret))!; string path = FileFor(provider); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EditorSession.AtomicWrite(path, iv.Concat(encrypted).ToArray());
    }
    public void Delete(string provider)
    {
        if (provider is not ("anthropic" or "openai")) return;
        string path = FileFor(provider); if (File.Exists(path)) File.Delete(path);
        using var store = Store(); if (store.ContainsAlias(Alias(provider))) store.DeleteEntry(Alias(provider));
    }
}
