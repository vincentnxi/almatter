using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Almatter.App.Models;

namespace Almatter.App.Services;

/// <summary>
/// Remembers the logged-in session across restarts, so the app doesn't ask
/// for a password every time it's relaunched. Encrypted with Windows'
/// DPAPI (ProtectedData, scoped to the current Windows user account) —
/// only this Windows user, on this machine, can ever decrypt the file.
/// Stores the session token, never the password itself: the password is
/// only ever used once, for the login call that produces this token.
/// </summary>
internal static class SessionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Almatter",
        "session.dat");

    // DPAPI's optional "entropy" — an extra ingredient mixed into the
    // encryption. Not a secret in itself (DPAPI's real security comes from
    // the Windows user's own protected key); it just keeps this file from
    // being decryptable by unrelated code that also happens to call DPAPI
    // under the same Windows account.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Almatter.SessionStore.v1");

    public static void Save(Session session)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.SerializeToUtf8Bytes(session);
            var encrypted = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
        }
        catch
        {
            // Not being able to remember the session just means a normal login next time.
        }
    }

    public static Session? Load()
    {
        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var json = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Session>(json);
        }
        catch
        {
            // Missing file, corrupt data, a DPAPI key no longer valid (e.g.
            // restored from a different machine/profile) — just means no
            // remembered session, not a crash.
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
            // Best-effort — a leftover file just gets overwritten by the next Save.
        }
    }
}
