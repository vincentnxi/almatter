using System;
using System.IO;

namespace Almatter.App.Services;

/// <summary>
/// Writes a file so that it is either the old contents or the new ones, never
/// half of each. A plain write truncates the file first and fills it after: a
/// crash, a power cut or a killed process in between leaves an empty or cut-off
/// file. For settings.json that meant every preference back to its default at
/// the next launch (the file is written on every change, so the window for it
/// is not small), and for the saved sign-in it meant a login screen.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] contents)
    {
        // A name of its own each time, so two saves at once can't share one.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // Nothing more to do about a leftover temporary file.
            }
            throw;
        }
    }

    public static void WriteAllText(string path, string contents) =>
        WriteAllBytes(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents));
}
