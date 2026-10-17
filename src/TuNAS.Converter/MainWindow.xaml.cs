using System.Windows;
using TuNAS.Converter.Models;
using TuNAS.Converter.ViewModels;

namespace TuNAS.Converter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Enable(this);
        DataContext = _viewModel;
        _viewModel.EditRequested = OpenCutWindow;
    }

    public void AddFiles(IEnumerable<string> paths) => _viewModel.AddPaths(paths);

    private bool OpenCutWindow(ClipItem clip)
    {
        var window = new CutWindow(clip) { Owner = this };
        if (window.ShowDialog() != true) return false;
        clip.SetRemovedRanges(window.RemovedRanges);
        return true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_viewModel.IsConverting
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_viewModel.IsConverting) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _viewModel.AddPaths(paths);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsConverting || MoreButton.ContextMenu == null) return;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        MoreButton.ContextMenu.IsOpen = true;
    }
}
