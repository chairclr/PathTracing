using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace RayTracing.Graphics;

public class SwapChain : IDisposable
{
    private bool _disposed;

    internal GraphicsDevice GraphicsDevice;

    internal KhrSwapchain KhrExtension;

    internal SwapchainKHR VkSwapchain;

    public Format Format { get; internal set; }

    public Extent2D Extent { get; internal set; }

    internal SwapChain(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;

        if (!VkAPI.API.TryGetDeviceExtension(GraphicsDevice.Instance, GraphicsDevice.Device, out KhrExtension))
        {
            throw new NotSupportedException("VK_KHR_swapchain extension not found");
        }
    }

    internal static SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
    {
        foreach (SurfaceFormatKHR availableFormat in availableFormats)
        {
            if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return availableFormat;
            }
        }

        return availableFormats.First();
    }

    internal static PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
    {
        foreach (PresentModeKHR availablePresentMode in availablePresentModes)
        {
            if (availablePresentMode == PresentModeKHR.MailboxKhr)
            {
                return availablePresentMode;
            }
        }

        return PresentModeKHR.ImmediateKhr;
    }

    internal static Extent2D ChooseSwapExtent(IWindow window, SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }
        else
        {
            Vector2D<int> framebufferSize = window.FramebufferSize;

            Extent2D actualExtent = new()
            {
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y
            };

            actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

            return actualExtent;
        }
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

partial class ResourceFactory
{
    public unsafe SwapChain CreateSwapChain(SurfaceKHR surface)
    {
        SwapChain swapChain = new SwapChain(GraphicsDevice);

        GraphicsDevice.SwapChainSupportDetails swapChainSupport = GraphicsDevice.QuerySwapChainSupport(GraphicsDevice.PhysicalDevice);

        SurfaceFormatKHR surfaceFormat = SwapChain.ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        PresentModeKHR presentMode = SwapChain.ChoosePresentMode(swapChainSupport.PresentModes);
        Extent2D extent = SwapChain.ChooseSwapExtent(GraphicsDevice.Renderer.Window, swapChainSupport.Capabilities);

        uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
        if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
        {
            imageCount = swapChainSupport.Capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,

            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
        };

        GraphicsDevice.QueueFamilyIndices indices = GraphicsDevice.FindQueueFamilies(GraphicsDevice.PhysicalDevice);
        uint* queueFamilyIndices = stackalloc uint[] { indices.GraphicsAndComputeFamily!.Value, indices.PresentFamily!.Value };

        if (indices.GraphicsAndComputeFamily != indices.PresentFamily)
        {
            createInfo = createInfo with
            {
                ImageSharingMode = SharingMode.Concurrent,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = queueFamilyIndices,
            };
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        createInfo = createInfo with
        {
            PreTransform = swapChainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,

            OldSwapchain = default
        };

        if (swapChain.KhrExtension.CreateSwapchain(GraphicsDevice.Device, in createInfo, null, out swapChain.VkSwapchain) != Result.Success)
        {
            throw new Exception("Failed to create SwapChain");
        }

        swapChain.KhrExtension.GetSwapchainImages(GraphicsDevice.Device, swapChain.VkSwapchain, ref imageCount, null);

        swapChain.Format = surfaceFormat.Format;
        swapChain.Extent = extent;

        return swapChain;
    }
}
