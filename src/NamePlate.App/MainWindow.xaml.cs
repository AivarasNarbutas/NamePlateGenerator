using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using NamePlateApp.ViewModels;

namespace NamePlateApp;

/// <summary>View only: everything is bound to <see cref="MainViewModel"/>. The code here drives the camera,
/// which is purely presentational state of the Helix viewport.</summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.FitViewRequested += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => View.ZoomExtents(200));
        vm.HolderProfileReset += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => ProfileEditor.Fit());
        Loaded += (_, _) => LookAlong(new Vector3D(0, 0, -1), new Vector3D(0, 1, 0));
    }

    /// <summary>Sets the viewing direction, then frames everything.</summary>
    private void LookAlong(Vector3D look, Vector3D up)
    {
        if (View.Camera is not { } cam) return;
        cam.LookDirection = look;
        cam.UpDirection = up;
        View.ZoomExtents(200);
    }

    private void ViewFront_Click(object sender, RoutedEventArgs e) => LookAlong(new Vector3D(0, 0, -1), new Vector3D(0, 1, 0));
    private void ViewBack_Click(object sender, RoutedEventArgs e) => LookAlong(new Vector3D(0, 0, 1), new Vector3D(0, 1, 0));
    private void ViewIso_Click(object sender, RoutedEventArgs e) => LookAlong(new Vector3D(-0.35, 0.75, -0.7), new Vector3D(0, 0, 1));
    private void ViewFit_Click(object sender, RoutedEventArgs e) => View.ZoomExtents(200);
}
