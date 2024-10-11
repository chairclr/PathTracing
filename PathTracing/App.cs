using Silk.NET.Windowing;

namespace PathTracing;

public class App : IDisposable
{
    private bool _disposed;

    public IWindow Window { get; private set; }

    public App()
    {
        WindowOptions windowOptions = WindowOptions.DefaultVulkan with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            Title = "Vulkan PathTracing"
        };

        Window = Silk.NET.Windowing.Window.Create(windowOptions);
        Window.Initialize();

        if (Window.VkSurface is null)
        {
            throw new Exception("Platform does not support Vulkan");
        }
    }

    public void Run()
    {
        Window.Run();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
