using System;
using System.IO;
using System.Runtime.CompilerServices;
using PdfEditor.Services;

namespace PdfEditor.Tests;

/// <summary>
/// Redirects every PdfEditor test that touches AppPaths-backed storage
/// (window settings, recent files, zoom prefs, preferences) into a temp
/// directory so the user's real ~/.config/PdfEditor/ is never touched.
///
/// Without this, GUI / golden-path / keyboard-shortcut tests that
/// instantiate <see cref="PdfEditor.ViewModels.MainWindowViewModel"/>
/// would write fixture-PDF paths into the user's persisted state, and
/// the next real-app launch would try to restore from the now-deleted
/// /tmp paths — observed in v2.1.0-rc4 manual testing as ~80% CPU idle
/// loop and 925 MB RSS climb.
///
/// Using <see cref="ModuleInitializerAttribute"/> means this runs
/// exactly once per test-assembly load, before any test executes.
/// </summary>
internal static class TestEnvironmentInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfEditorTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppPaths.OverrideForTests(root);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* best effort */ }
        };
    }
}
