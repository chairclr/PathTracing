using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracing.Logging;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace RayTracing.Graphics;

public unsafe class GraphicsDevice : IDisposable
{
    private bool _disposed;

    public Renderer Renderer;

    public Instance Instance;
    public PhysicalDevice PhysicalDevice;
    public Device Device;

    public Queue GraphicsQueue;
    public Queue PresentQueue;
    public Queue ComputeQueue;

    public ResourceFactory ResourceFactory { get; private set; }

    public GraphicsDevice(Renderer renderer)
    {
        Renderer = renderer;

        ResourceFactory = new ResourceFactory(this);
    }

    public void Init()
    {
        SetupDebugMessenger();
        PickPhysicalDevice();
        CreateLogicalDevice();
    }

    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        VkAPI.API.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemoryProperties memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                return (uint)i;
            }
        }

        throw new Exception("failed to find suitable memory type!");
    }

    private void CreateInstance()
    {
        #if DEBUG
        if (!VkAPI.CheckValidationLayerSupport())
        {
            throw new Exception("Validation layers requested but not supported");
        }
#endif

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Hello Triangle"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        InstanceCreateInfo createInfo = new()
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
        PopulateDebugUtilsMessengerCreateInfo(ref debugCreateInfo);
        createInfo.PNext = &debugCreateInfo;
#else
        createInfo.EnabledLayerCount = 0;
        createInfo.PNext = null;
#endif

        if (VkAPI.API.CreateInstance(in createInfo, null, out Instance) != Result.Success)
        {
            throw new Exception("Failed to create Vulkan Instance");
        }

        Marshal.FreeHGlobal((nint)appInfo.PApplicationName);
        Marshal.FreeHGlobal((nint)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

#if DEBUG
        SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
#endif
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
        VkAPI.API.GetDeviceQueue(Device, indices.GraphicsAndComputeFamily!.Value, 0, out ComputeQueue);

#if DEBUG
        SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
#endif

        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

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

    public QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        QueueFamilyIndices indices = new();

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
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit) && queueFamily.QueueFlags.HasFlag(QueueFlags.ComputeBit))
            {
                indices.GraphicsAndComputeFamily = i;
            }

            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            Renderer.KhrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, Renderer.Surface, out Bool32 presentSupport);

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

    public SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice PhysicalDevice)
    {
        SwapChainSupportDetails details = new();

        Renderer.KhrSurface!.GetPhysicalDeviceSurfaceCapabilities(PhysicalDevice, Renderer.Surface, out details.Capabilities);

        uint formatCount = 0;
        Renderer.KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Renderer.Surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                Renderer.KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Renderer.Surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        Renderer.KhrSurface.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Renderer.Surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
            {
                Renderer.KhrSurface.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Renderer.Surface, ref presentModeCount, formatsPtr);
            }

        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    private string[] GetRequiredExtensions()
    {
        byte** glfwExtensions = Renderer.Window.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
        string[] extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

#if DEBUG
        return [.. extensions, ExtDebugUtils.ExtensionName];
#else
        return extensions;
#endif
    }

    private ExtDebugUtils? DebugUtils;
    private DebugUtilsMessengerEXT DebugMessenger;

    private void SetupDebugMessenger()
    {
#if DEBUG
        if (!VkAPI.API.TryGetInstanceExtension(Instance, out DebugUtils)) return;

        DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
        PopulateDebugUtilsMessengerCreateInfo(ref debugCreateInfo);
        
        if (DebugUtils!.CreateDebugUtilsMessenger(Instance, in debugCreateInfo, null, out DebugMessenger) != Result.Success)
        {
            throw new Exception("Failed to create DebugMessenger");
        }
#endif
    }

    public void PopulateDebugUtilsMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT debugCreateInfo)
    {
        debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        debugCreateInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        debugCreateInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        debugCreateInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
    }

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        string? message = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage);

        if ((messageSeverity & DebugUtilsMessageSeverityFlagsEXT.InfoBitExt) != 0 || (messageSeverity & DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt) != 0)
        {
            Logger.LogInformation(message);
        }

        if ((messageSeverity & DebugUtilsMessageSeverityFlagsEXT.WarningBitExt) != 0)
        {
            Logger.LogWarning(message);
        }

        if ((messageSeverity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
        {
            Logger.LogCritical(message);
        }

        return Vk.False;
    }

    public struct QueueFamilyIndices
    {
        public uint? GraphicsFamily { get; set; }
        public uint? GraphicsAndComputeFamily { get; set; }
        public uint? PresentFamily { get; set; }

        public bool IsComplete()
        {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }

    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            VkAPI.API.DestroyDevice(Device, null);

#if DEBUG
            DebugUtils?.DestroyDebugUtilsMessenger(Instance, DebugMessenger, null);
#endif

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
