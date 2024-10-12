using Silk.NET.Windowing;
using PathTracing.Graphics;
using Silk.NET.Maths;

namespace PathTracing;

public class App : IDisposable
{
    private bool _disposed;

    public IWindow Window { get; private set; } = null!;

    public Renderer Renderer { get; private set; } = null!;

    public void Run()
    {
        InitWindow();
        InitRenderer();

        Window.Run();
    }

    private void InitWindow()
    {
        WindowOptions options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(1920, 1080),
            Title = "Vulkan",
        };

        Window = Silk.NET.Windowing.Window.Create(options);
        Window.Initialize();

        if (Window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan.");
        }

        Window.Render += (dt) =>
        {
            Renderer.Render((float)dt);
        };
    }

    private void InitRenderer()
    {
        Renderer = new Renderer(Window);

        Renderer.Init();
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
