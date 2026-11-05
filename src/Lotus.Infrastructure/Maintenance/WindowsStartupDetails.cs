using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Lotus.Core.Maintenance;
using Lotus.Core.Optimization;

namespace Lotus.Infrastructure.Maintenance;

// Read-only: the program behind a startup item, its publisher (file details) and its Authenticode signature, checked by
// Windows itself (WinVerifyTrust, no revocation download so it stays quick and offline).
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupDetails : IStartupDetails
{
    public StartupDetails Inspect(StartupEntry entry)
    {
        var path = StartupCommand.ExecutableOf(entry.Command);
        if (path is not null && !Path.IsPathRooted(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : path + ".exe");
        if (path is null || !File.Exists(path)) return new(path, null, SignatureState.Unknown, null);
        string? publisher = null;
        try { publisher = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() is { Length: > 0 } c ? c : null; } catch (Exception ex) when (ex is IOException or FileNotFoundException) { }
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return new(path, publisher, SignatureState.Unknown, null);
        var state = Verify(path);
        string? signer = null;
        if (state == SignatureState.Valid)
            try
            {
#pragma warning disable SYSLIB0057 // reading the Authenticode signer of a file has no X509CertificateLoader equivalent
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                signer = cert.GetNameInfo(X509NameType.SimpleName, false);
            }
            catch (System.Security.Cryptography.CryptographicException) { }
        return new(path, publisher, state, signer);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoData { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks;
        public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
    private static Guid GenericVerify = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint UiNone = 2, RevokeNone = 0, ChoiceFile = 1, ActionVerify = 1, ActionClose = 2, CacheOnlyUrl = 0x1000, DisableMd2Md4 = 0x2000;
    private const int NoSignature = unchecked((int)0x800B0100), FileNotSigned = unchecked((int)0x800B0003);

    public static SignatureState Verify(string path)
    {
        var file = new FileInfoData { cbStruct = (uint)Marshal.SizeOf<FileInfoData>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoData>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new TrustData { cbStruct = (uint)Marshal.SizeOf<TrustData>(), dwUIChoice = UiNone, fdwRevocationChecks = RevokeNone, dwUnionChoice = ChoiceFile, pFile = filePtr, dwStateAction = ActionVerify, dwProvFlags = CacheOnlyUrl | DisableMd2Md4 };
            var result = WinVerifyTrust(IntPtr.Zero, ref GenericVerify, ref data);
            data.dwStateAction = ActionClose; WinVerifyTrust(IntPtr.Zero, ref GenericVerify, ref data);
            return result switch { 0 => SignatureState.Valid, NoSignature or FileNotSigned => SignatureState.Unsigned, _ => SignatureState.Invalid };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return SignatureState.Unknown; }
        finally { Marshal.DestroyStructure<FileInfoData>(filePtr); Marshal.FreeHGlobal(filePtr); }
    }
}
