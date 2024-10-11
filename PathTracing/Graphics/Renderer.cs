
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;

namespace PathTracing.Graphics;

public unsafe class Renderer : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    private Instance Instance;

    public Renderer(IWindow window)
    {
        Window = window;
    }

    public void Load()
    {
        CreateInstance();
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

    private string[] GetRequiredExtensions()
    {
        byte** glfwExtensions = Window!.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
        string[] extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

#if DEBUG
        return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
#else
        return extensions;
#endif
    }

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        Console.WriteLine($"[VALIDATION] " + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

        return Vk.False;
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
