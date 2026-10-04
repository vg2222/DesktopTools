using System.IO;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

namespace DesktopTools;

internal sealed partial class AppController
{
    private ScreenshotLibrary? library;

    internal ScreenshotLibrary Library => library ??= new ScreenshotLibrary(smoke
        ? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-library")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "library"));

    /// <summary>Every finished screenshot goes to the session history; with the opt-in library on it is also stored and indexed for search.</summary>
    private void RememberCapture(BitmapSource image)
    {
        CaptureHistory.Add(image);
        if (!Settings.ScreenshotLibraryEnabled) return;
        string? language = null;
        try { language = LocalOcr.SelectLanguage(LocalOcr.Languages, Settings.ScreenTextLanguage)?.Tag; } catch (Exception) { }
        _ = Library.AddAsync(image, language, Settings.ScreenshotLibraryLimit);
    }

    internal void OpenScreenshotLibrary() => OpenUtility("ScreenshotLibrary", () => new ScreenshotLibraryWindow(this, Library));
}
