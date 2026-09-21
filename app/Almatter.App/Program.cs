using Avalonia;
using System;
using Almatter.App.Diagnostics;
using Almatter.App.Services;

namespace Almatter.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLogger.Install();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("StartWithClassicDesktopLifetime", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [RenderingMode()] })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Processor or graphics card, per AppSettings.UseGpuRendering — which
    /// defaults to the processor, and says at length why.
    ///
    /// Read straight off disk rather than through the app's settings object:
    /// this runs before Avalonia is initialized and therefore before anything
    /// that could hold one, and the rendering back end has to be chosen while
    /// the AppBuilder is being assembled. SettingsStore.Load is a plain file
    /// read that falls back to defaults on anything at all going wrong, so a
    /// missing or corrupt settings.json means the processor path, not a crash
    /// before the first window.
    /// </summary>
    private static Win32RenderingMode RenderingMode()
    {
        try
        {
            return SettingsStore.Load().UseGpuRendering
                ? Win32RenderingMode.AngleEgl
                : Win32RenderingMode.Software;
        }
        catch
        {
            return Win32RenderingMode.Software;
        }
    }
}
