using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using PdfEditor.Services;

namespace PdfEditor.Models;

/// <summary>
/// Window settings for persistence across sessions.
/// Saves window position, size, and state.
///
/// See Issue #23: Save and restore window position and size
/// Uses AppPaths for cross-platform storage locations (Issues #265, #266, #267).
/// </summary>
public class WindowSettings
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 800;
    public bool IsMaximized { get; set; }

    // Use AppPaths for cross-platform correct paths
    private static string SettingsPath => AppPaths.WindowSettingsPath;

    /// <summary>
    /// Load settings from disk, or return default settings if not found.
    /// </summary>
    public static WindowSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<WindowSettings>(json);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Ignore errors, use defaults
        }

        return new WindowSettings();
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            // AppPaths.ConfigDir ensures directory exists
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    /// <summary>
    /// Apply settings to a window.
    /// </summary>
    public void ApplyTo(Window window)
    {
        // Set size first
        if (Width > 0 && Height > 0)
        {
            window.Width = Width;
            window.Height = Height;
        }

        // Set position if valid (not off-screen)
        if (IsPositionValid())
        {
            window.Position = new PixelPoint((int)X, (int)Y);
        }

        // Set maximized state after position/size
        if (IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// Capture current window state.
    /// </summary>
    public void CaptureFrom(Window window)
    {
        IsMaximized = window.WindowState == WindowState.Maximized;

        // Only save position/size if not maximized
        if (!IsMaximized)
        {
            X = window.Position.X;
            Y = window.Position.Y;
            Width = window.Width;
            Height = window.Height;
        }
    }

    /// <summary>
    /// Check if the saved position would place the window on a visible screen.
    /// </summary>
    private bool IsPositionValid()
    {
        // Basic sanity check - position should be reasonable
        // A more complete implementation would check against actual screen bounds
        return X >= -100 && Y >= -100 && X < 10000 && Y < 10000;
    }
}
