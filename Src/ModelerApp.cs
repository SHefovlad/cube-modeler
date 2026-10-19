using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using SkiaSharp;

namespace Modeler;

/// <summary>Точка входа приложения-просмотрщика (окно, без промежуточного PNG).</summary>
public sealed class ModelerApp : Application
{
    private readonly string _cubesPath;
    private readonly SKColor? _color;
    private readonly bool _isoVariants;
    private readonly bool _classic;
    private readonly bool _gpu;

    public ModelerApp(string cubesPath, SKColor? color, bool isoVariants = false, bool classic = false,
        bool gpu = true)
    {
        _cubesPath = cubesPath;
        _color = color;
        _isoVariants = isoVariants;
        _classic = classic;
        _gpu = gpu;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Классический режим — как было. Новый — плитки, которые меняются местами.
            // Режим углов живёт только в классическом окне.
            desktop.MainWindow = _classic || _isoVariants
                ? new ViewerWindow(_cubesPath, _color, _isoVariants)
                : new SwapViewerWindow(_cubesPath, _color, _gpu);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
