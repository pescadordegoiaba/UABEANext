using Avalonia.Controls;
using Avalonia.Interactivity;
using UABEANext4.ViewModels.Dialogs;

namespace UABEANext4.Views.Dialogs;

public partial class Il2CppDumpView : UserControl
{
    public Il2CppDumpView()
    {
        InitializeComponent();
    }

    private void RunDump_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Il2CppDumpViewModel vm)
        {
            vm.BtnRun_Click();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Il2CppDumpViewModel vm)
        {
            vm.BtnCancel_Click();
        }
    }

    private void UseMono_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Il2CppDumpViewModel vm)
        {
            vm.BtnUseMono_Click();
        }
    }
}