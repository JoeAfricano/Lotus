namespace Lotus.App;

// Google Drive sign-in kept in the Windows Credential Locker (per user, encrypted by Windows).
public sealed class WindowsTokenVault : Lotus.Infrastructure.Backup.ITokenVault
{
    private const string Resource = "Lotus.GoogleDrive";
    private static readonly Windows.Security.Credentials.PasswordVault Vault = new();
    public string? Read()
    {
        try { var c = Vault.FindAllByResource(Resource).FirstOrDefault(); if (c is null) return null; c.RetrievePassword(); return c.UserName + "\n" + c.Password; }
        catch (Exception) { return null; } // nothing stored yet throws
    }
    public void Write(string value)
    {
        Clear();
        var parts = value.Split('\n', 2);
        Vault.Add(new Windows.Security.Credentials.PasswordCredential(Resource, parts[0], parts.Length > 1 ? parts[1] : ""));
    }
    public void Clear()
    {
        try { foreach (var c in Vault.FindAllByResource(Resource)) Vault.Remove(c); } catch (Exception) { }
    }
}
