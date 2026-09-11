using System;
using System.Runtime.InteropServices;

namespace Almatter.App.Interop;

/// <summary>
/// P/Invoke bridge to the Rust <c>almatter-core</c> crate. Every call into the
/// Mattermost API, the SQLite cache and the offline sync engine goes through
/// this boundary — the UI layer never talks to the network or the database
/// directly.
/// </summary>
/// <remarks>
/// Uses classic <see cref="DllImportAttribute"/> (runtime marshaling) rather
/// than <c>LibraryImport</c>'s compile-time source generator: the generator
/// emits unsafe code to get zero-overhead marshaling, which this project
/// doesn't need for the call volume here. DllImport keeps this whole class
/// free of the `unsafe` keyword and the AllowUnsafeBlocks project setting.
/// </remarks>
internal static class NativeCore
{
    private const string LibraryName = "almatter_ffi";

    [DllImport(LibraryName, EntryPoint = "almatter_core_version")]
    private static extern IntPtr almatter_core_version();

    [DllImport(LibraryName, EntryPoint = "almatter_core_free_string")]
    private static extern void almatter_core_free_string(IntPtr ptr);

    [DllImport(LibraryName, EntryPoint = "almatter_core_call", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr almatter_core_call([MarshalAs(UnmanagedType.LPUTF8Str)] string requestJson);

    /// <summary>Round-trips a call through the Rust core to prove the bridge works end to end.</summary>
    public static string GetCoreVersion()
    {
        var ptr = almatter_core_version();
        try
        {
            return Marshal.PtrToStringUTF8(ptr) ?? "?";
        }
        finally
        {
            almatter_core_free_string(ptr);
        }
    }

    /// <summary>
    /// Every Mattermost API call goes through this single JSON-in/JSON-out
    /// entry point — see almatter-core's <c>dispatch</c> module for the
    /// request/response shapes.
    /// </summary>
    public static string Call(string requestJson)
    {
        var ptr = almatter_core_call(requestJson);
        try
        {
            return Marshal.PtrToStringUTF8(ptr) ?? "{\"ok\":false,\"error\":\"no response from core\"}";
        }
        finally
        {
            almatter_core_free_string(ptr);
        }
    }
}
