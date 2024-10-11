
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace PathTracing.Graphics;

public unsafe class Renderer : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    private Instance Instance;

    private KhrSurface? KhrSurface;
    private SurfaceKHR Surface;

    private PhysicalDevice PhysicalDevice;
    private Device Device;

    private Queue GraphicsQueue;
    private Queue PresentQueue;

    private KhrSwapchain? KhrSwapChain;
    private SwapchainKHR SwapChain;
    private Image[]? SwapChainImages;
    private Format SwapChainImageFormat;
    private Extent2D SwapChainExtent;

    public Renderer(IWindow window)
    {
        Window = window;
    }

    public void Load()
    {
        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
    }

    public void Update(float deltaTime)
    {

    }

    public void Render(float deltaTime)
    {

    }

    private void CreateInstance()
    {
#if DEBUG
        if (!VkAPI.CheckValidationLayerSupport())
        {
            throw new Exception("Validation layers requested but not supported");
        }
#endif

        ApplicationInfo appInfo = new ApplicationInfo()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Hello Triangle"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        InstanceCreateInfo createInfo = new InstanceCreateInfo()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo
        };

        string[] extensions = GetRequiredExtensions();
        createInfo.EnabledExtensionCount = (uint)extensions.Length;
        createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);

#if DEBUG
        createInfo.EnabledLayerCount = (uint)VkAPI.ValidationLayers.Length;
        createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(VkAPI.ValidationLayers);

        DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
        debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        debugCreateInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        debugCreateInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        debugCreateInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        debugCreateInfo.PNext = &debugCreateInfo;
#else
        createInfo.EnabledLayerCount = 0;
        createInfo.PNext = null;
#endif

        if (VkAPI.API.CreateInstance(in createInfo, null, out Instance) != Result.Success)
        {
            throw new Exception("Failed to create Vulkan Instance");
        }

        Marshal.FreeHGlobal((IntPtr)appInfo.PApplicationName);
        Marshal.FreeHGlobal((IntPtr)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

#if DEBUG
        SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
#endif
    }

    private void CreateSurface()
    {
        if (!VkAPI.API.TryGetInstanceExtension<KhrSurface>(Instance, out KhrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found");
        }

        if (Window.Native?.X11 is not null)
        {
            XcbSurfaceCreateInfoKHR createInfo = new XcbSurfaceCreateInfoKHR()
            {
                SType = StructureType.XcbSurfaceCreateInfoKhr,
                Window = (nint)Window.Native?.X11?.Window!
            };

            VkAPI.API.TryGetInstanceExtension(Instance, out KhrXcbSurface api);
            api.CreateXcbSurface(Instance, &createInfo, (AllocationCallbacks*)null, (SurfaceKHR*)Surface.Handle);
        }
        else if (Window.Native?.Wayland is not null)
        {
            WaylandSurfaceCreateInfoKHR createInfo = new WaylandSurfaceCreateInfoKHR()
            {
                SType = StructureType.WaylandSurfaceCreateInfoKhr,
                Surface = (nint*)Window.Native?.Wayland?.Surface!
            };

            VkAPI.API.TryGetInstanceExtension(Instance, out KhrWaylandSurface api);
            api.CreateWaylandSurface(Instance, &createInfo, (AllocationCallbacks*)null, (SurfaceKHR*)Surface.Handle);
       }
    }

    private void PickPhysicalDevice()
    {
        foreach (PhysicalDevice device in VkAPI.API.GetPhysicalDevices(Instance))
        {
            if (IsDeviceSuitable(device))
            {
                PhysicalDevice = device;
                break;
            }
        }

        if (PhysicalDevice.Handle == 0)
        {
            throw new Exception("No physical device available");
        }
    }

    private void CreateLogicalDevice()
    {
        QueueFamilyIndices indices = FindQueueFamilies(PhysicalDevice);

        uint[] uniqueQueueFamilies = new uint[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };
        uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

        using GlobalMemory mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
        DeviceQueueCreateInfo* queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

        float queuePriority = 1.0f;
        for (int i = 0; i < uniqueQueueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new()
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        PhysicalDeviceFeatures deviceFeatures = new();

        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length,
            PQueueCreateInfos = queueCreateInfos,

            PEnabledFeatures = &deviceFeatures,

            EnabledExtensionCount = (uint)VkAPI.DeviceExtensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(VkAPI.DeviceExtensions)
        };

#if DEBUG
        createInfo.EnabledLayerCount = (uint)VkAPI.ValidationLayers.Length;
        createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(VkAPI.ValidationLayers);
#else
            createInfo.EnabledLayerCount = 0;
#endif

        if (VkAPI.API.CreateDevice(PhysicalDevice, in createInfo, null, out Device) != Result.Success)
        {
            throw new Exception("failed to create logical Device!");
        }

        VkAPI.API.GetDeviceQueue(Device, indices.GraphicsFamily!.Value, 0, out GraphicsQueue);
        VkAPI.API.GetDeviceQueue(Device, indices.PresentFamily!.Value, 0, out PresentQueue);

#if DEBUG
        SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
#endif

        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

    }

    private void CreateSwapChain()
    {
        SwapChainSupportDetails swapChainSupport = QuerySwapChainSupport(PhysicalDevice);

        SurfaceFormatKHR surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        PresentModeKHR presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
        Extent2D extent = ChooseSwapExtent(swapChainSupport.Capabilities);

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

        QueueFamilyIndices indices = FindQueueFamilies(PhysicalDevice);
        uint* queueFamilyIndices = stackalloc[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };

        if (indices.GraphicsFamily != indices.PresentFamily)
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

        if (!VkAPI.API.TryGetDeviceExtension(Instance, Device, out KhrSwapChain))
        {
            throw new NotSupportedException("VK_KHR_swapchain extension not found");
        }

        if (KhrSwapChain!.CreateSwapchain(Device, in createInfo, null, out SwapChain) != Result.Success)
        {
            throw new Exception("Failed to create SwapChain");
        }

        KhrSwapChain.GetSwapchainImages(Device, SwapChain, ref imageCount, null);
        SwapChainImages = new Image[imageCount];
        fixed (Image* swapChainImagesPtr = SwapChainImages)
        {
            KhrSwapChain.GetSwapchainImages(Device, SwapChain, ref imageCount, swapChainImagesPtr);
        }

        SwapChainImageFormat = surfaceFormat.Format;
        SwapChainExtent = extent;
    }

    private SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
    {
        foreach (SurfaceFormatKHR availableFormat in availableFormats)
        {
            if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return availableFormat;
            }
        }

        return availableFormats[0];
    }

    private PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
    {
        foreach (PresentModeKHR availablePresentMode in availablePresentModes)
        {
            if (availablePresentMode == PresentModeKHR.MailboxKhr)
            {
                return availablePresentMode;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }
        else
        {
            Vector2D<int> framebufferSize = Window.FramebufferSize;

            Extent2D actualExtent = new Extent2D()
            {
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y
            };

            actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
            actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

            return actualExtent;
        }
    }

    private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
    {
        SwapChainSupportDetails details = new SwapChainSupportDetails();

        KhrSurface!.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, Surface, out details.Capabilities);

        uint formatCount = 0;
        KhrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, Surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                KhrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, Surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        KhrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, Surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
            {
                KhrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, Surface, ref presentModeCount, formatsPtr);
            }

        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    private bool IsDeviceSuitable(PhysicalDevice device)
    {
        QueueFamilyIndices indices = FindQueueFamilies(device);

        bool extensionsSupported = CheckDeviceExtensionsSupport(device);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            SwapChainSupportDetails swapChainSupport = QuerySwapChainSupport(device);
            swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
        }

        return indices.IsComplete() && extensionsSupported && swapChainAdequate;
    }

    private bool CheckDeviceExtensionsSupport(PhysicalDevice device)
    {
        uint extentionsCount = 0;
        VkAPI.API.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, null);

        ExtensionProperties[] availableExtensions = new ExtensionProperties[extentionsCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
        {
            VkAPI.API.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, availableExtensionsPtr);
        }

        HashSet<string?> availableExtensionNames = availableExtensions.Select(extension => Marshal.PtrToStringAnsi((IntPtr)extension.ExtensionName)).ToHashSet();

        return VkAPI.DeviceExtensions.All(availableExtensionNames.Contains);

    }

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        QueueFamilyIndices indices = new QueueFamilyIndices();

        uint queueFamilityCount = 0;
        VkAPI.API.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, null);

        QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[queueFamilityCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            VkAPI.API.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, queueFamiliesPtr);
        }


        uint i = 0;
        foreach (QueueFamilyProperties queueFamily in queueFamilies)
        {
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            KhrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out var presentSupport);

            if (presentSupport)
            {
                indices.PresentFamily = i;
            }

            if (indices.IsComplete())
            {
                break;
            }

            i++;
        }

        return indices;
    }

    private string[] GetRequiredExtensions()
    {
        string[] extensions = [KhrXcbSurface.ExtensionName, KhrWaylandSurface.ExtensionName];
#if DEBUG
        return [..extensions, ExtDebugUtils.ExtensionName];
#else
        return extensions;
#endif
    }

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        Console.WriteLine($"[VALIDATION] " + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

        return Vk.False;
    }

    struct QueueFamilyIndices
    {
        public uint? GraphicsFamily { get; set; }
        public uint? PresentFamily { get; set; }

        public bool IsComplete()
        {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }

    struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
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
