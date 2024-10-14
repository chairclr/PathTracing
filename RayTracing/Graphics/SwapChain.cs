using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace RayTracing.Graphics;

public unsafe class SwapChain : IDisposable
{
    private bool _disposed;

    internal GraphicsDevice GraphicsDevice;

    internal KhrSwapchain KhrSwapchainExtension;

    internal KhrSurface KhrSurfaceExtension;

    internal SwapchainKHR VkSwapchain;

    public Format Format { get; internal set; }

    public Extent2D Extent { get; internal set; }

    public SurfaceKHR Surface { get; internal set; }

    internal SwapChain(GraphicsDevice graphicsDevice)
    {
        GraphicsDevice = graphicsDevice;

        if (!VkAPI.API.TryGetDeviceExtension(GraphicsDevice.Instance, GraphicsDevice.Device, out KhrSwapchainExtension))
        {
            throw new NotSupportedException("VK_KHR_swapchain extension not found");
        }

        if (!VkAPI.API.TryGetInstanceExtension<KhrSurface>(GraphicsDevice.Instance, out KhrSurfaceExtension))
        {
            throw new NotSupportedException("KHR_surface extension not found");
        }
    }

    public void Recreate()
    {
        KhrSwapchainExtension.DestroySwapchain(GraphicsDevice.Device, VkSwapchain, null);

        CreateSwapChainCore();
    }
    
    internal void CreateSwapChainCore()
    {
        Surface = GraphicsDevice.Renderer.Window.VkSurface!.Create<AllocationCallbacks>(GraphicsDevice.Instance.ToHandle(), null).ToSurface();

        SwapChainSupportDetails swapChainSupport = QuerySwapChainSupport(GraphicsDevice.PhysicalDevice);

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
            Surface = Surface,

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

        if (KhrSwapchainExtension.CreateSwapchain(GraphicsDevice.Device, in createInfo, null, out VkSwapchain) != Result.Success)
        {
            throw new Exception("Failed to create SwapChain");
        }

        Format = surfaceFormat.Format;
        Extent = extent;
    }

    public SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice PhysicalDevice)
    {
        SwapChainSupportDetails details = new();

        KhrSurfaceExtension!.GetPhysicalDeviceSurfaceCapabilities(PhysicalDevice, Surface, out details.Capabilities);

        uint formatCount = 0;
        KhrSurfaceExtension.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                KhrSurfaceExtension.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        KhrSurfaceExtension.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
            {
                KhrSurfaceExtension.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, ref presentModeCount, formatsPtr);
            }
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
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
            KhrSwapchainExtension.DestroySwapchain(GraphicsDevice.Device, VkSwapchain, null);
            KhrSwapchainExtension.Dispose();

            _disposed = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    
    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }
}

