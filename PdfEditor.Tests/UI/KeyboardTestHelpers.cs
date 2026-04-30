using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Test helpers for simulating keyboard input on Avalonia windows.
/// Extensions for Window to send key presses with modifiers and dispatch flushing.
/// </summary>
public static class KeyboardTestHelpers
{
    /// <summary>
    /// Simulate a key press on the window with optional modifiers.
    /// Flushes the dispatcher to allow ReactiveUI commands to process.
    /// </summary>
    public static async Task PressKeyAsync(
        this Window window,
        Key key,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        // Create and dispatch a KeyDown event. RoutedEvent must be set explicitly
        // because Avalonia's KeyEventArgs doesn't infer it from the Route alone.
        var keyDown = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Route = RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            Key = key,
            KeyModifiers = ConvertToKeyModifiers(modifiers),
        };

        window.RaiseEvent(keyDown);

        // Also raise KeyUp so handlers that listen to either event observe the
        // full press sequence.
        var keyUp = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Route = RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            Key = key,
            KeyModifiers = ConvertToKeyModifiers(modifiers),
        };
        window.RaiseEvent(keyUp);

        // Flush the dispatcher to allow commands to execute
        await FlushDispatcherAsync();
    }

    /// <summary>
    /// Type a text string by sending individual character key presses.
    /// Automatically determines if Shift is needed for special characters.
    /// </summary>
    public static async Task TypeTextAsync(this Window window, string text)
    {
        foreach (var ch in text)
        {
            var (key, modifiers) = CharToKeyAndModifiers(ch);
            await window.PressKeyAsync(key, modifiers);
            // Small delay between keystrokes for realism
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Send Enter key press and flush dispatcher.
    /// </summary>
    public static async Task PressEnterAsync(this Window window)
    {
        await window.PressKeyAsync(Key.Return);
    }

    /// <summary>
    /// Send Escape key press and flush dispatcher.
    /// </summary>
    public static async Task PressEscapeAsync(this Window window)
    {
        await window.PressKeyAsync(Key.Escape);
    }

    /// <summary>
    /// Flush the Avalonia dispatcher to allow pending operations to complete.
    /// </summary>
    public static async Task FlushDispatcherAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(() => tcs.SetResult(true), DispatcherPriority.Background);
        await tcs.Task;
    }

    /// <summary>
    /// Convert character to Key and required modifiers (e.g., 'A' -> (Key.A, Shift)).
    /// </summary>
    private static (Key Key, RawInputModifiers Modifiers) CharToKeyAndModifiers(char ch)
    {
        return ch switch
        {
            // Letters
            >= 'a' and <= 'z' => (KeyForChar(char.ToUpper(ch)), RawInputModifiers.None),
            >= 'A' and <= 'Z' => (KeyForChar(ch), RawInputModifiers.Shift),

            // Numbers
            '0' => (Key.D0, RawInputModifiers.None),
            '1' => (Key.D1, RawInputModifiers.None),
            '2' => (Key.D2, RawInputModifiers.None),
            '3' => (Key.D3, RawInputModifiers.None),
            '4' => (Key.D4, RawInputModifiers.None),
            '5' => (Key.D5, RawInputModifiers.None),
            '6' => (Key.D6, RawInputModifiers.None),
            '7' => (Key.D7, RawInputModifiers.None),
            '8' => (Key.D8, RawInputModifiers.None),
            '9' => (Key.D9, RawInputModifiers.None),

            // Symbols
            ' ' => (Key.Space, RawInputModifiers.None),
            '!' => (Key.D1, RawInputModifiers.Shift),
            '@' => (Key.D2, RawInputModifiers.Shift),
            '#' => (Key.D3, RawInputModifiers.Shift),
            '$' => (Key.D4, RawInputModifiers.Shift),
            '%' => (Key.D5, RawInputModifiers.Shift),
            '^' => (Key.D6, RawInputModifiers.Shift),
            '&' => (Key.D7, RawInputModifiers.Shift),
            '*' => (Key.D8, RawInputModifiers.Shift),
            '(' => (Key.D9, RawInputModifiers.Shift),
            ')' => (Key.D0, RawInputModifiers.Shift),
            '-' => (Key.OemMinus, RawInputModifiers.None),
            '_' => (Key.OemMinus, RawInputModifiers.Shift),
            '=' => (Key.OemPlus, RawInputModifiers.None),
            '+' => (Key.OemPlus, RawInputModifiers.Shift),
            '[' => (Key.OemOpenBrackets, RawInputModifiers.None),
            '{' => (Key.OemOpenBrackets, RawInputModifiers.Shift),
            ']' => (Key.OemCloseBrackets, RawInputModifiers.None),
            '}' => (Key.OemCloseBrackets, RawInputModifiers.Shift),
            ';' => (Key.OemSemicolon, RawInputModifiers.None),
            ':' => (Key.OemSemicolon, RawInputModifiers.Shift),
            '\'' => (Key.OemQuotes, RawInputModifiers.None),
            '"' => (Key.OemQuotes, RawInputModifiers.Shift),
            ',' => (Key.OemComma, RawInputModifiers.None),
            '<' => (Key.OemComma, RawInputModifiers.Shift),
            '.' => (Key.OemPeriod, RawInputModifiers.None),
            '>' => (Key.OemPeriod, RawInputModifiers.Shift),
            '/' => (Key.OemQuestion, RawInputModifiers.None),
            '?' => (Key.OemQuestion, RawInputModifiers.Shift),
            '\\' => (Key.OemBackslash, RawInputModifiers.None),
            '|' => (Key.OemBackslash, RawInputModifiers.Shift),
            '`' => (Key.OemTilde, RawInputModifiers.None),
            '~' => (Key.OemTilde, RawInputModifiers.Shift),

            _ => throw new NotSupportedException($"Character '{ch}' is not supported for typing"),
        };
    }

    /// <summary>
    /// Map uppercase letter to Key enum.
    /// </summary>
    private static Key KeyForChar(char ch) => ch switch
    {
        'A' => Key.A,
        'B' => Key.B,
        'C' => Key.C,
        'D' => Key.D,
        'E' => Key.E,
        'F' => Key.F,
        'G' => Key.G,
        'H' => Key.H,
        'I' => Key.I,
        'J' => Key.J,
        'K' => Key.K,
        'L' => Key.L,
        'M' => Key.M,
        'N' => Key.N,
        'O' => Key.O,
        'P' => Key.P,
        'Q' => Key.Q,
        'R' => Key.R,
        'S' => Key.S,
        'T' => Key.T,
        'U' => Key.U,
        'V' => Key.V,
        'W' => Key.W,
        'X' => Key.X,
        'Y' => Key.Y,
        'Z' => Key.Z,
        _ => throw new ArgumentException($"Unknown character: {ch}"),
    };

    /// <summary>
    /// Convert RawInputModifiers to Avalonia KeyModifiers for KeyEventArgs.
    /// </summary>
    private static KeyModifiers ConvertToKeyModifiers(RawInputModifiers raw)
    {
        var result = KeyModifiers.None;
        if (raw.HasFlag(RawInputModifiers.Control))
            result |= KeyModifiers.Control;
        if (raw.HasFlag(RawInputModifiers.Alt))
            result |= KeyModifiers.Alt;
        if (raw.HasFlag(RawInputModifiers.Shift))
            result |= KeyModifiers.Shift;
        if (raw.HasFlag(RawInputModifiers.Meta))
            result |= KeyModifiers.Meta;
        return result;
    }
}
