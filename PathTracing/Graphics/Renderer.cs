
using Silk.NET.Windowing;

namespace PathTracing.Graphics;

public class Renderer : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    public Renderer(IWindow window) 
    {  
        Window = window;

        Window.Load += Load;
        Window.Render += Render;
    }

    public void Load()
    {

    }

    public void Update(double deltaTime)
    {

    }

    public void Render(double deltaTime)
    {

    }

    private void CreateInstance()
    {
        // TODO: Create Vulkan instance
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
