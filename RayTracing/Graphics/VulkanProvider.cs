using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace RayTracing.Graphics;

public class VkAPI
{
    public static Vk API;

    public static readonly string[] ValidationLayers = new[]
    {
        "VK_LAYER_KHRONOS_validation"
    };

    public static readonly string[] DeviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName
    };

    static VkAPI()
    {
        API = Vk.GetApi();
    }

    public unsafe static bool CheckValidationLayerSupport()
    {
        uint layerCount = 0;
        API.EnumerateInstanceLayerProperties(ref layerCount, null);
        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            API.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((IntPtr)layer.LayerName)).ToHashSet();

        return ValidationLayers.All(availableLayerNames.Contains);
    }
}
