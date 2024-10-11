using Silk.NET.Windowing;
using PathTracing.Graphics;
using Silk.NET.Windowing.Sdl;

namespace PathTracing;

public class App : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    public Renderer Renderer { get; private set; } = null!;

    public App()
    {
        SdlWindowing.Use();
        WindowOptions windowOptions = WindowOptions.Default with
        {
            Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            Title = "Vulkan PathTracing",
            API = GraphicsAPI.None,
            IsVisible = true
        };

        Window = Silk.NET.Windowing.Window.Create(windowOptions);

        Window.Load += Load;
        Window.Render += (dt) => 
        {
            Renderer.Render((float)dt);
        };
    }

    public void Load()
    {
        Renderer = new Renderer(Window);

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
