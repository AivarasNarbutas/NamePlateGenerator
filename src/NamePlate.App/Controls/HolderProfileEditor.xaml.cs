using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Clipper2Lib;
using HelixToolkit.Wpf;
using NamePlate.Core;
using NamePlateApp.Converters;
using NamePlateApp.ViewModels;

namespace NamePlateApp.Controls;

/// <summary>
/// 2D editor for the holder cross-section (same interaction as the RoboPack part configurator): the grey rectangle
/// is the plate section (front face on the left), every profile point has a marker, clicking a marker selects it and
/// the selected point gets red (X) and green (Y) arrows to drag. Values go straight into the point view-models.
/// </summary>
public partial class HolderProfileEditor : UserControl
{
    private static readonly Material PlateMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xE8, 0xB4, 0x2C)));
    private static readonly Material HolderMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xDE)));
    private static readonly Material MarkerMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)));
    private static readonly Material SelectedMat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xD2, 0x7D, 0x2D)));

    private readonly List<(SphereVisual3D Visual, HolderPointViewModel Point)> _markers = new();
    private readonly List<(BindableTranslateManipulator M, HolderPointViewModel Point)> _manipulators = new();
    private bool _syncingManipulators;
    private Point _downAt;
    private bool _downOnHandle;
    private INotifyCollectionChanged? _observed;
    private readonly HashSet<HolderPointViewModel> _subscribed = new();
    private bool _fitPending = true;

    public HolderProfileEditor()
    {
        InitializeComponent();
        // the tab unloads/reloads this control on every switch: keep the user's zoom, fit only when asked
        Loaded += (_, _) => { Redraw(); if (_fitPending) Fit(); };
    }

    // ---------------- dependency properties ----------------

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(HolderProfileEditor), new PropertyMetadata(null, (d, e) => ((HolderProfileEditor)d).OnPointsChanged(e)));

    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    public static readonly DependencyProperty SelectedPointProperty = DependencyProperty.Register(
        nameof(SelectedPoint), typeof(HolderPointViewModel), typeof(HolderProfileEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HolderProfileEditor)d).OnSelectionChanged()));

    public HolderPointViewModel? SelectedPoint { get => (HolderPointViewModel?)GetValue(SelectedPointProperty); set => SetValue(SelectedPointProperty, value); }

    public static readonly DependencyProperty PlateThicknessProperty = DependencyProperty.Register(
        nameof(PlateThickness), typeof(double), typeof(HolderProfileEditor), new PropertyMetadata(3.0, (d, _) => ((HolderProfileEditor)d).Redraw()));

    public double PlateThickness { get => (double)GetValue(PlateThicknessProperty); set => SetValue(PlateThicknessProperty, value); }

    public static readonly DependencyProperty PlateHeightProperty = DependencyProperty.Register(
        nameof(PlateHeight), typeof(double), typeof(HolderProfileEditor), new PropertyMetadata(40.0, (d, _) => ((HolderProfileEditor)d).Redraw()));

    public double PlateHeight { get => (double)GetValue(PlateHeightProperty); set => SetValue(PlateHeightProperty, value); }

    /// <summary>Frames the whole profile (call after the profile was replaced). While the editor is hidden the
    /// viewport has no size, so the fit waits until it is shown again.</summary>
    public void Fit()
    {
        if (!IsLoaded || View.ActualWidth < 1 || View.ActualHeight < 1) { _fitPending = true; return; }
        _fitPending = false;
        View.ZoomExtents(0);
    }

    // ---------------- change tracking ----------------

    private IEnumerable<HolderPointViewModel> PointList => Points?.OfType<HolderPointViewModel>() ?? Enumerable.Empty<HolderPointViewModel>();

    private void OnPointsChanged(DependencyPropertyChangedEventArgs e)
    {
        if (_observed != null) _observed.CollectionChanged -= Points_CollectionChanged;
        _observed = e.NewValue as INotifyCollectionChanged;
        if (_observed != null) _observed.CollectionChanged += Points_CollectionChanged;
        Resubscribe();
        RebuildMarkers();
    }

    private void Points_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Resubscribe(); // also covers Clear(), which does not report the removed items
        RebuildMarkers();
    }

    /// <summary>Listens to exactly the points currently in the list.</summary>
    private void Resubscribe()
    {
        var now = PointList.ToHashSet();
        foreach (var p in _subscribed.Where(p => !now.Contains(p)).ToList()) { p.PropertyChanged -= Point_PropertyChanged; _subscribed.Remove(p); }
        foreach (var p in now.Where(p => _subscribed.Add(p))) p.PropertyChanged += Point_PropertyChanged;
    }

    private void Point_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(HolderPointViewModel.X) or nameof(HolderPointViewModel.Y))) return;
        Redraw();
        SyncManipulators();
    }

    private void OnSelectionChanged()
    {
        UpdateMarkerColours();
        RebuildManipulators();
    }

    // ---------------- drawing ----------------

    private double Extent()
    {
        var pts = PointList.ToList();
        if (pts.Count == 0) return Math.Max(PlateHeight, 10);
        double w = pts.Max(p => p.X) - pts.Min(p => p.X), h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        return Math.Max(Math.Max(w, h), Math.Max(PlateHeight, 10));
    }

    private void Redraw()
    {
        var group = new Model3DGroup();

        // plate cross-section
        var plate = new TriMesh();
        plate.Cap(Extrusion.Rect(0, 0, PlateThickness, PlateHeight), 0.02, true);
        group.Children.Add(PreviewSceneConverter.ToModel(plate, PlateMat, 0));

        // holder fill + outline
        var pts = PointList.ToList();
        var lines = new Point3DCollection();
        if (pts.Count >= 3)
        {
            var poly = new PathD(pts.Select(p => new PointD(p.X, p.Y)));
            var region = Clipper.Union(new PathsD { poly }, new PathsD(), Clipper2Lib.FillRule.NonZero, Extrusion.Precision);
            var fill = new TriMesh();
            fill.Cap(region, 0, true);
            group.Children.Add(PreviewSceneConverter.ToModel(fill, HolderMat, 0));
        }
        for (int i = 0; i < pts.Count && pts.Count >= 2; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Count];
            lines.Add(new Point3D(a.X, a.Y, 0.05));
            lines.Add(new Point3D(b.X, b.Y, 0.05));
        }
        Fill.Content = group;
        Outline.Points = lines;

        double r = MarkerRadius();
        foreach (var (visual, point) in _markers)
        {
            visual.Center = new Point3D(point.X, point.Y, 0.1);
            visual.Radius = r;
        }
    }

    private double MarkerRadius() => Extent() * 0.014;

    private void RebuildMarkers()
    {
        foreach (var (v, _) in _markers) View.Children.Remove(v);
        _markers.Clear();
        double r = MarkerRadius();
        foreach (var p in PointList)
        {
            var v = new SphereVisual3D { Center = new Point3D(p.X, p.Y, 0.1), Radius = r, ThetaDiv = 16, PhiDiv = 8, Material = MarkerMat };
            _markers.Add((v, p));
            View.Children.Add(v);
        }
        UpdateMarkerColours();
        RebuildManipulators();
        Redraw();
    }

    private void UpdateMarkerColours()
    {
        foreach (var (v, p) in _markers) v.Material = ReferenceEquals(p, SelectedPoint) ? SelectedMat : MarkerMat;
    }

    // ---------------- selection by click ----------------

    private void View_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _downAt = e.GetPosition(View);
        var hits = View.Viewport != null ? Viewport3DHelper.FindHits(View.Viewport, _downAt) : new List<PointHitResult>();
        _downOnHandle = hits.Any(h => h.Visual is Manipulator);
        if (_downOnHandle) return;
        var marker = hits.Select(h => _markers.FirstOrDefault(m => ReferenceEquals(m.Visual, h.Visual))).FirstOrDefault(m => m.Visual != null);
        if (marker.Visual != null)
        {
            SetCurrentValue(SelectedPointProperty, marker.Point);
            _downOnHandle = true; // do not deselect on mouse up
            e.Handled = true;
        }
    }

    private void View_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // a click (not a drag) on empty space clears the selection
        if (!_downOnHandle && (e.GetPosition(View) - _downAt).Length < 3) SetCurrentValue(SelectedPointProperty, null);
    }

    // ---------------- drag handles on the selected point ----------------

    private void RebuildManipulators()
    {
        foreach (var (m, _) in _manipulators)
        {
            DependencyPropertyDescriptor.FromProperty(Manipulator.PositionProperty, typeof(Manipulator))?.RemoveValueChanged(m, Manipulator_PositionChanged);
            View.Children.Remove(m);
        }
        _manipulators.Clear();
        var p = SelectedPoint;
        if (p == null || !PointList.Contains(p)) return;

        double len = Extent() * 0.12, dia = MarkerRadius() * 0.7;
        foreach (var (dir, colour) in new[] { (new Vector3D(1, 0, 0), Colors.Red), (new Vector3D(0, 1, 0), Colors.Green) })
        {
            var m = new BindableTranslateManipulator
            {
                Direction = dir, Length = len, Diameter = dia, Color = colour,
                Material = new DiffuseMaterial(new SolidColorBrush(colour)),
            };
            var matrix = Matrix3D.Identity; matrix.Translate(new Vector3D(p.X, p.Y, 0.2));
            m.Transform = new MatrixTransform3D(matrix);
            m.TargetTransform = new MatrixTransform3D(matrix);
            m.Position = new Point3D(p.X, p.Y, 0.2);
            DependencyPropertyDescriptor.FromProperty(Manipulator.PositionProperty, typeof(Manipulator))?.AddValueChanged(m, Manipulator_PositionChanged);
            _manipulators.Add((m, p));
            View.Children.Add(m);
        }
    }

    private void Manipulator_PositionChanged(object? sender, EventArgs e)
    {
        if (_syncingManipulators || sender is not BindableTranslateManipulator m) return;
        var (_, p) = _manipulators.FirstOrDefault(x => ReferenceEquals(x.M, m));
        if (p == null) return;
        var dir = m.Direction; dir.Normalize();
        // keep the move on the arrow's axis
        var move = m.Position - new Point3D(p.X, p.Y, 0.2);
        double along = Vector3D.DotProduct(move, dir);
        _syncingManipulators = true;
        try
        {
            if (dir.X != 0) p.X += along * dir.X;
            if (dir.Y != 0) p.Y += along * dir.Y;
        }
        finally { _syncingManipulators = false; }
        SyncManipulators();
    }

    /// <summary>Re-anchors both arrows at their point (after a drag, a typed value or a plate change).</summary>
    private void SyncManipulators()
    {
        bool was = _syncingManipulators;
        _syncingManipulators = true;
        try
        {
            foreach (var (m, p) in _manipulators)
            {
                var matrix = Matrix3D.Identity; matrix.Translate(new Vector3D(p.X, p.Y, 0.2));
                if (m.Transform is MatrixTransform3D t && !t.IsFrozen) t.Matrix = matrix; else m.Transform = new MatrixTransform3D(matrix);
                if (m.TargetTransform is MatrixTransform3D tt && !tt.IsFrozen) tt.Matrix = matrix; else m.TargetTransform = new MatrixTransform3D(matrix);
                m.Position = new Point3D(p.X, p.Y, 0.2);
            }
        }
        finally { _syncingManipulators = was; }
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    // for tests
    internal int MarkerCount => _markers.Count;
    internal IReadOnlyList<BindableTranslateManipulator> Handles => _manipulators.Select(m => m.M).ToList();
}
