using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PdfEditor.ViewModels;
using System;

namespace PdfEditor.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();

        // Wire up commands to close the window when DataContext is set
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel)
        {
            viewModel.SaveCommand.Subscribe(_ => Close());
            viewModel.CancelCommand.Subscribe(_ => Close());
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
