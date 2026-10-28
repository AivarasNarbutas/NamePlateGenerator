using NamePlate.Core;
using NamePlateApp.Mvvm;

namespace NamePlateApp.ViewModels;

/// <summary>
/// One corner of the holder profile. The stored values are offsets from a plate face/edge (<see cref="XFrom"/>,
/// <see cref="XOffset"/> ...); <see cref="X"/>/<see cref="Y"/> are the resulting absolute coordinates the 2D editor
/// draws and drags. Switching an anchor keeps the point where it is and recomputes the offset.
/// </summary>
public sealed class HolderPointViewModel : ObservableObject
{
    private readonly Func<HolderFrame> _frame;
    private readonly Action _edited;
    private readonly HolderPoint _p;
    private int _number;

    public HolderPointViewModel(HolderPoint point, Func<HolderFrame> frame, Action edited)
    {
        _p = point.Copy();
        _frame = frame;
        _edited = edited;
    }

    public int Number { get => _number; set => SetField(ref _number, value); }

    public XAnchor XFrom
    {
        get => _p.XFrom;
        set
        {
            if (_p.XFrom == value) return;
            double abs = X;
            _p.XFrom = value;
            _p.X = Round(abs - _frame().XOf(value));
            Changed(nameof(XFrom), nameof(XOffset));
        }
    }

    public double XOffset
    {
        get => _p.X;
        set { if (_p.X == value || !double.IsFinite(value)) return; _p.X = value; Changed(nameof(XOffset)); }
    }

    public YAnchor YFrom
    {
        get => _p.YFrom;
        set
        {
            if (_p.YFrom == value) return;
            double abs = Y;
            _p.YFrom = value;
            _p.Y = Round(abs - _frame().YOf(value));
            Changed(nameof(YFrom), nameof(YOffset));
        }
    }

    public double YOffset
    {
        get => _p.Y;
        set { if (_p.Y == value || !double.IsFinite(value)) return; _p.Y = value; Changed(nameof(YOffset)); }
    }

    /// <summary>Absolute position across the plate thickness (0 = front face).</summary>
    public double X
    {
        get => _frame().XOf(_p.XFrom) + _p.X;
        set => XOffset = Round(value - _frame().XOf(_p.XFrom));
    }

    /// <summary>Absolute position along the plate height (0 = bottom edge).</summary>
    public double Y
    {
        get => _frame().YOf(_p.YFrom) + _p.Y;
        set => YOffset = Round(value - _frame().YOf(_p.YFrom));
    }

    public HolderPoint ToModel() => _p.Copy();

    /// <summary>The plate (or clearance) changed: the anchors moved, so the absolute position did too.</summary>
    internal void FrameChanged()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
    }

    private void Changed(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        _edited();
    }

    /// <summary>Dragging gives arbitrary doubles; keep the stored offsets at 0.01 mm.</summary>
    private static double Round(double v) => Math.Round(v, 2);
}
