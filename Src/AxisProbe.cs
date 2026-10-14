namespace Modeler;

/// <summary>Отладка: печатает оси камеры для нескольких комбинаций yaw/pitch.</summary>
public static class AxisProbe
{
    public static void Run()
    {
        foreach (var (yaw, pitch) in new[] { (45.0, 52.0), (45.0, 0.0), (135.0, 52.0), (45.0, -52.0) })
        {
            var axes = Projector.IsoAxesByYawPitch(yaw, pitch);
            Console.WriteLine(
                $"yaw={yaw,5} pitch={pitch,5}  forward=({axes.Forward.X,7:0.000},{axes.Forward.Y,7:0.000},{axes.Forward.Z,7:0.000})" +
                $"  up=({axes.Up.X,7:0.000},{axes.Up.Y,7:0.000},{axes.Up.Z,7:0.000})" +
                $"  right=({axes.Right.X,7:0.000},{axes.Right.Y,7:0.000},{axes.Right.Z,7:0.000})");
        }
    }
}
