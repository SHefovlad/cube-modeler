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

    public ModelerApp(string cubesPath, SKColor? color)
    {
        _cubesPath = cubesPath;
        _color = color;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ViewerWindow(_cubesPath, _color);

        base.OnFrameworkInitializationCompleted();
    }
}
