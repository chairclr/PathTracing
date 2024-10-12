
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PathTracing.Logging;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;

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
    private const int FRAMEBUFFER_COUNT = 2;

    private ImageView[]? swapChainImageViews;
    private Framebuffer[]? swapChainFramebuffers;

    private RenderPass renderPass;
    private PipelineLayout pipelineLayout;
    private Pipeline graphicsPipeline;

    private CommandPool commandPool;
    private CommandBuffer[]? commandBuffers;

    private Semaphore[]? imageAvailableSemaphores;
    private Semaphore[]? renderFinishedSemaphores;
    private Fence[]? inFlightFences;
    private Fence[]? imagesInFlight;
    private int currentFrame = 0;

    public Renderer(IWindow window)
    {
        Window = window;
    }

    public void Init()
    {
        CreateInstance();
        SetupDebugMessenger();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateGraphicsPipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();
    }

    public void Update(float deltaTime)
    {

    }

    public void Render(float deltaTime)
    {
        DrawFrame(deltaTime);
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
        debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        debugCreateInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        debugCreateInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        debugCreateInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
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

    private void CreateSurface()
    {
        if (!VkAPI.API.TryGetInstanceExtension<KhrSurface>(Instance, out KhrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found");
        }

        Surface = Window.VkSurface!.Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();
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
        uint* queueFamilyIndices = stackalloc uint[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };

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
        fixed (Image* SwapChainImagesPtr = SwapChainImages)
        {
            KhrSwapChain.GetSwapchainImages(Device, SwapChain, ref imageCount, SwapChainImagesPtr);
        }

        SwapChainImageFormat = surfaceFormat.Format;
        SwapChainExtent = extent;
    }

    private void CreateImageViews()
    {
        swapChainImageViews = new ImageView[SwapChainImages!.Length];

        for (int i = 0; i < SwapChainImages.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = SwapChainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = SwapChainImageFormat,
                Components =
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                }

            };

            if (VkAPI.API.CreateImageView(Device, in createInfo, null, out swapChainImageViews[i]) != Result.Success)
            {
                throw new Exception("Failed to create image views");
            }
        }
    }

    private void CreateRenderPass()
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = SwapChainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };

        AttachmentReference colorAttachmentRef = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
        };

        SubpassDependency dependency = new()
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        RenderPassCreateInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        if (VkAPI.API.CreateRenderPass(Device, in renderPassInfo, null, out renderPass) != Result.Success)
        {
            throw new Exception("failed to create render pass!");
        }
    }

    private void CreateGraphicsPipeline()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "Compiled");
        byte[] vertShaderCode = File.ReadAllBytes(Path.Combine(path, "Basic", "VertexShader.spirv"));
        byte[] fragShaderCode = File.ReadAllBytes(Path.Combine(path, "Basic", "PixelShader.spirv"));

        ShaderModule vertShaderModule = CreateShaderModule(vertShaderCode);
        ShaderModule pixelShaderModule = CreateShaderModule(fragShaderCode);

        PipelineShaderStageCreateInfo vertShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("VSMain")
        };

        PipelineShaderStageCreateInfo pixelShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = pixelShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("PSMain")
        };

        PipelineShaderStageCreateInfo* shaderStages = stackalloc[]
        {
            vertShaderStageInfo,
            pixelShaderStageInfo
        };

        PipelineVertexInputStateCreateInfo vertexInputInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            VertexAttributeDescriptionCount = 1,
            PVertexAttributeDescriptions = Vertex.GetVertexAttributeDescriptions()
        };

        PipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false,
        };

        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = SwapChainExtent.Width,
            Height = SwapChainExtent.Height,
            MinDepth = 0,
            MaxDepth = 1,
        };

        Rect2D scissor = new()
        {
            Offset = { X = 0, Y = 0 },
            Extent = SwapChainExtent,
        };

        PipelineViewportStateCreateInfo viewportState = new()
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            PViewports = &viewport,
            ScissorCount = 1,
            PScissors = &scissor,
        };

        PipelineRasterizationStateCreateInfo rasterizer = new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1,
            CullMode = CullModeFlags.BackBit,
            FrontFace = FrontFace.Clockwise,
            DepthBiasEnable = false,
        };

        PipelineMultisampleStateCreateInfo multisampling = new()
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };

        PipelineColorBlendAttachmentState colorBlendAttachment = new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false,
        };

        PipelineColorBlendStateCreateInfo colorBlending = new()
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            LogicOp = LogicOp.Copy,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment,
        };

        colorBlending.BlendConstants[0] = 0;
        colorBlending.BlendConstants[1] = 0;
        colorBlending.BlendConstants[2] = 0;
        colorBlending.BlendConstants[3] = 0;

        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 0,
            PushConstantRangeCount = 0,
        };

        if (VkAPI.API.CreatePipelineLayout(Device, in pipelineLayoutInfo, null, out pipelineLayout) != Result.Success)
        {
            throw new Exception("failed to create pipeline layout!");
        }

        GraphicsPipelineCreateInfo pipelineInfo = new()
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = shaderStages,
            PVertexInputState = &vertexInputInfo,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisampling,
            PColorBlendState = &colorBlending,
            Layout = pipelineLayout,
            RenderPass = renderPass,
            Subpass = 0,
            BasePipelineHandle = default
        };

        if (VkAPI.API.CreateGraphicsPipelines(Device, default, 1, in pipelineInfo, null, out graphicsPipeline) != Result.Success)
        {
            throw new Exception("Failed to create graphics pipeline");
        }

        VkAPI.API.DestroyShaderModule(Device, pixelShaderModule, null);
        VkAPI.API.DestroyShaderModule(Device, vertShaderModule, null);

        SilkMarshal.Free((nint)vertShaderStageInfo.PName);
        SilkMarshal.Free((nint)pixelShaderStageInfo.PName);
    }

    private void CreateFramebuffers()
    {
        swapChainFramebuffers = new Framebuffer[swapChainImageViews!.Length];

        for (int i = 0; i < swapChainImageViews.Length; i++)
        {
            ImageView attachment = swapChainImageViews[i];

            FramebufferCreateInfo framebufferInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = SwapChainExtent.Width,
                Height = SwapChainExtent.Height,
                Layers = 1,
            };

            if (VkAPI.API.CreateFramebuffer(Device, in framebufferInfo, null, out swapChainFramebuffers[i]) != Result.Success)
            {
                throw new Exception("failed to create framebuffer!");
            }
        }
    }

    private void CreateCommandPool()
    {
        QueueFamilyIndices queueFamiliyIndicies = FindQueueFamilies(PhysicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamiliyIndicies.GraphicsFamily!.Value,
        };

        if (VkAPI.API.CreateCommandPool(Device, in poolInfo, null, out commandPool) != Result.Success)
        {
            throw new Exception("failed to create command pool!");
        }
    }

    private void CreateCommandBuffers()
    {
        commandBuffers = new CommandBuffer[swapChainFramebuffers!.Length];

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)commandBuffers.Length,
        };

        fixed (CommandBuffer* commandBuffersPtr = commandBuffers)
        {
            if (VkAPI.API.AllocateCommandBuffers(Device, in allocInfo, commandBuffersPtr) != Result.Success)
            {
                throw new Exception("failed to allocate command buffers!");
            }
        }


        for (int i = 0; i < commandBuffers.Length; i++)
        {
            CommandBufferBeginInfo beginInfo = new()
            {
                SType = StructureType.CommandBufferBeginInfo,
            };

            if (VkAPI.API.BeginCommandBuffer(commandBuffers[i], in beginInfo) != Result.Success)
            {
                throw new Exception("Failed to begin command buffer");
            }

            RenderPassBeginInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = renderPass,
                Framebuffer = swapChainFramebuffers[i],
                RenderArea =
                {
                    Offset = { X = 0, Y = 0 },
                    Extent = SwapChainExtent,
                }
            };

            ClearValue clearColor = new()
            {
                Color = new() { Float32_0 = 1f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 1f },
            };

            renderPassInfo.ClearValueCount = 1;
            renderPassInfo.PClearValues = &clearColor;

            VkAPI.API.CmdBeginRenderPass(commandBuffers[i], &renderPassInfo, SubpassContents.Inline);

            VkAPI.API.CmdBindPipeline(commandBuffers[i], PipelineBindPoint.Graphics, graphicsPipeline);

            VkAPI.API.CmdDraw(commandBuffers[i], 3, 1, 0, 0);

            VkAPI.API.CmdEndRenderPass(commandBuffers[i]);

            if (VkAPI.API.EndCommandBuffer(commandBuffers[i]) != Result.Success)
            {
                throw new Exception("failed to record command buffer!");
            }

        }
    }

    private void CreateSyncObjects()
    {
        imageAvailableSemaphores = new Semaphore[FRAMEBUFFER_COUNT];
        renderFinishedSemaphores = new Semaphore[FRAMEBUFFER_COUNT];
        inFlightFences = new Fence[FRAMEBUFFER_COUNT];
        imagesInFlight = new Fence[SwapChainImages!.Length];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (int i = 0; i < FRAMEBUFFER_COUNT; i++)
        {
            if (VkAPI.API.CreateSemaphore(Device, in semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                VkAPI.API.CreateSemaphore(Device, in semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                VkAPI.API.CreateFence(Device, in fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("failed to create synchronization objects for a frame!");
            }
        }
    }

    private void DrawFrame(float delta)
    {
        VkAPI.API.WaitForFences(Device, 1, in inFlightFences![currentFrame], true, ulong.MaxValue);

        uint imageIndex = 0;
        KhrSwapChain!.AcquireNextImage(Device, SwapChain, ulong.MaxValue, imageAvailableSemaphores![currentFrame], default, ref imageIndex);

        if (imagesInFlight![imageIndex].Handle != default)
        {
            VkAPI.API.WaitForFences(Device, 1, in imagesInFlight[imageIndex], true, ulong.MaxValue);
        }
        imagesInFlight[imageIndex] = inFlightFences[currentFrame];

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
        };

        Semaphore* waitSemaphores = stackalloc[] { imageAvailableSemaphores[currentFrame] };
        PipelineStageFlags* waitStages = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };

        CommandBuffer buffer = commandBuffers![imageIndex];

        submitInfo = submitInfo with
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,

            CommandBufferCount = 1,
            PCommandBuffers = &buffer
        };

        Semaphore* signalSemaphores = stackalloc[] { renderFinishedSemaphores![currentFrame] };
        submitInfo = submitInfo with
        {
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores,
        };

        VkAPI.API.ResetFences(Device, 1, in inFlightFences[currentFrame]);

        if (VkAPI.API.QueueSubmit(GraphicsQueue, 1, in submitInfo, inFlightFences[currentFrame]) != Result.Success)
        {
            throw new Exception("failed to submit draw command buffer!");
        }

        SwapchainKHR* swapChains = stackalloc[] { SwapChain };
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,

            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,

            SwapchainCount = 1,
            PSwapchains = swapChains,

            PImageIndices = &imageIndex
        };

        KhrSwapChain.QueuePresent(PresentQueue, in presentInfo);

        currentFrame = (currentFrame + 1) % FRAMEBUFFER_COUNT;
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        ShaderModuleCreateInfo createInfo = new()
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)code.Length,
        };

        ShaderModule shaderModule;

        fixed (byte* codePtr = code)
        {
            createInfo.PCode = (uint*)codePtr;

            if (VkAPI.API.CreateShaderModule(Device, in createInfo, null, out shaderModule) != Result.Success)
            {
                throw new Exception();
            }
        }

        return shaderModule;

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

    private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice PhysicalDevice)
    {
        SwapChainSupportDetails details = new();

        KhrSurface!.GetPhysicalDeviceSurfaceCapabilities(PhysicalDevice, Surface, out details.Capabilities);

        uint formatCount = 0;
        KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        KhrSurface.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
            {
                KhrSurface.GetPhysicalDeviceSurfacePresentModes(PhysicalDevice, Surface, ref presentModeCount, formatsPtr);
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
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            KhrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out Bool32 presentSupport);

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
        byte** glfwExtensions = Window.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
        string[] extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

#if DEBUG
        return [..extensions, ExtDebugUtils.ExtensionName];
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
        debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        debugCreateInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        debugCreateInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        debugCreateInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;

        if (DebugUtils!.CreateDebugUtilsMessenger(Instance, in debugCreateInfo, null, out DebugMessenger) != Result.Success)
        {
            throw new Exception("Failed to create DebugMessenger");
        }
#endif
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
            KhrSwapChain?.DestroySwapchain(Device, SwapChain, null);
            VkAPI.API.DestroyDevice(Device, null);

#if DEBUG
            DebugUtils?.DestroyDebugUtilsMessenger(Instance, DebugMessenger, null); 
#endif
            KhrSurface?.DestroySurface(Instance, Surface, null);
            VkAPI.API.DestroyInstance(Instance, null);

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
