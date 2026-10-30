using System.Runtime.InteropServices;
using System.Text;

namespace UuDok.Web.Security;

public interface ISecretStore
{
    bool IsAvailable { get; }
    void Set(string name, string secret);
    string? Get(string name);
    void Delete(string name);
}

/// <summary>
/// API-nøkler lagres i Windows Legitimasjonsbehandling (Credential Manager), aldri i filer, logger eller database.
/// På andre plattformer finnes ingen sikker lagring i denne versjonen, og skytjenester kan da ikke brukes.
/// </summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const string Prefix = "UuDok:";
    public bool IsAvailable => OperatingSystem.IsWindows();

    public void Set(string name, string secret)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("Sikker nøkkellagring finnes ikke på denne plattformen.");
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new CREDENTIAL
            {
                Type = 1, // CRED_TYPE_GENERIC
                TargetName = Prefix + name,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE (bare denne brukeren på denne maskinen)
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref cred, 0)) throw new InvalidOperationException("Kunne ikke lagre nøkkelen i Windows Legitimasjonsbehandling.");
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
            Array.Clear(bytes);
        }
    }

    public string? Get(string name)
    {
        if (!IsAvailable) return null;
        if (!CredRead(Prefix + name, 1, 0, out var ptr)) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
        }
        finally { CredFree(ptr); }
    }

    public void Delete(string name)
    {
        if (IsAvailable) CredDelete(Prefix + name, 1, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags; public uint Type; public string TargetName; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist;
        public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>Kun for tester.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _d = [];
    public bool IsAvailable => true;
    public void Set(string name, string secret) => _d[name] = secret;
    public string? Get(string name) => _d.GetValueOrDefault(name);
    public void Delete(string name) => _d.Remove(name);
}
