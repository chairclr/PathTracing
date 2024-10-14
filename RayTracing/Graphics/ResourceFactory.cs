namespace RayTracing.Graphics;

public partial class ResourceFactory : IDisposable
{
    private bool _disposed;

    internal GraphicsDevice GraphicsDevice;

    internal ResourceFactory(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;
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
