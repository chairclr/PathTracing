using Silk.NET.Windowing;
using PathTracing.Graphics;

namespace PathTracing;

public class App : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    public Renderer Renderer { get; private set; } = null!;

    public App()
    {
        WindowOptions windowOptions = WindowOptions.DefaultVulkan with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            Title = "Vulkan PathTracing"
        };

        Window = Silk.NET.Windowing.Window.Create(windowOptions);

        Window.Load += Load;
        Window.Render += (dt) => 
        {
            Renderer.Render((float)dt);
        };
        
        Window.Initialize();
    }

    public void Load()
    {
        Renderer = new Renderer(Window);

        if (Window.VkSurface is null)
        {
            throw new Exception("Platform does not support Vulkan");
        }

        Renderer.Load();
    }

    public void Run()
    {
        Window.Run();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            Renderer.Dispose();

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
