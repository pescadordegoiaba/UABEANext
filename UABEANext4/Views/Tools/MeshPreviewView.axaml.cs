using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UABEANext4.Views.Tools;

public partial class MeshPreviewView : UserControl
{
    public MeshPreviewView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        MeshViewer?.RaiseEvent(e);
        base.OnKeyDown(e);
    }
}