using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TuNAS.Converter.Models;
using TuNAS.Converter.ViewModels;

namespace TuNAS.Converter;

public partial class MainWindow : Window
{
    /// <summary>Datenformat für das Verschieben eines Clips innerhalb der Liste.</summary>
    private const string ClipDragFormat = "TunasClip";

    private readonly MainViewModel _viewModel = new();
    private ClipItem? _dragClip;
    private Point _dragStart;

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

    // ---------- Dateien von außen hineinziehen und Clips in der Liste verschieben ----------

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ClipDragFormat))
        {
            // Clip wird verschoben: Liste ordnet sich schon während des Ziehens live um.
            e.Effects = DragDropEffects.Move;
            if (e.Data.GetData(ClipDragFormat) is ClipItem dragged
                && FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is ClipItem target
                && !ReferenceEquals(dragged, target))
            {
                _viewModel.MoveClip(dragged, _viewModel.Clips.IndexOf(target));
            }
        }
        else
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !_viewModel.IsConverting
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ClipDragFormat) || _viewModel.IsConverting) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _viewModel.AddPaths(paths);
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        // Klicks auf Knöpfe (Schneiden, Pfeile, Entfernen) sind kein Ziehen.
        _dragClip = FindParent<ButtonBase>(source) == null
            ? FindParent<ListBoxItem>(source)?.DataContext as ClipItem
            : null;
        _dragStart = e.GetPosition(ClipList);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragClip == null || e.LeftButton != MouseButtonState.Pressed || _viewModel.IsConverting) return;
        var delta = e.GetPosition(ClipList) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance
            && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        var clip = _dragClip;
        _dragClip = null;
        DragDrop.DoDragDrop(ClipList, new DataObject(ClipDragFormat, clip), DragDropEffects.Move);
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e) => _dragClip = null;

    /// <summary>Sucht vom angeklickten Element aufwärts das nächste Element vom Typ T.</summary>
    private static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null && node is not T)
        {
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return node as T;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsConverting || MoreButton.ContextMenu == null) return;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = PlacementMode.Top;
        MoreButton.ContextMenu.IsOpen = true;
    }
}
