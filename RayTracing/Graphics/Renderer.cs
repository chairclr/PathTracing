using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RayTracing.Logging;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayTracing.Graphics;

public unsafe class Renderer : IDisposable
{
    private bool _disposed;

    public readonly IWindow Window;

    public SwapChain? SwapChain;

    public KhrSurface? KhrSurface;
    public SurfaceKHR Surface;

    public GraphicsDevice GraphicsDevice;
    private Device Device => GraphicsDevice.Device;

    private RenderPass renderPass;
    private PipelineLayout pipelineLayout;
    private Pipeline graphicsPipeline;

    private Buffer vertexBuffer;
    private DeviceMemory vertexBufferMemory;

    private FrameData[] Frames = null!;
    private FrameSemaphores[] Semaphores = null!;
    private uint FrameIndex = 0;
    private uint SemaphoreIndex = 0;

    private Pipeline ComputePipeline;
    private PipelineLayout ComputePipelineLayout;
    private Fence ComputeFence;
    private CommandPool ComputeCommandPool;
    private CommandBuffer ComputeCommandBuffer;

    private Vertex[] vertices = new Vertex[]
    {
        // First triangle
        new Vertex { pos = new Vector3(-1.0f, -1.0f, 1f), uv = new Vector2(0.0f, 0.0f) }, // Bottom-left
        new Vertex { pos = new Vector3(1.0f, -1.0f, 1f), uv = new Vector2(1.0f, 0.0f) },  // Bottom-right
        new Vertex { pos = new Vector3(-1.0f,  1.0f, 1f), uv = new Vector2(0.0f, 1.0f) }, // Top-left

        // Second triangle
        new Vertex { pos = new Vector3(-1.0f,  1.0f, 1f), uv = new Vector2(0.0f, 1.0f) }, // Top-left (reused)
        new Vertex { pos = new Vector3(1.0f, -1.0f, 1f), uv = new Vector2(1.0f, 0.0f) },  // Bottom-right (reused)
        new Vertex { pos = new Vector3(1.0f,  1.0f, 1f), uv = new Vector2(1.0f, 1.0f) },  // Top-right
    };


    public Renderer(IWindow window)
    {
        Window = window;

        GraphicsDevice = new GraphicsDevice(this);
    }

    public void Init()
    {
        GraphicsDevice.Init();
        CreateSurface();
        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();

        CreateResources();
        CreateDescriptorSetLayout();
        CreateDescriptorSets();

        CreateGraphicsPipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateVertexBuffer();
        CreateCommandBuffers();
        CreateSemaphores();
        CreateFences();
        CreateComputePipeline();
    }

    public void Update(float deltaTime)
    {

    }

    private Image ComputeImage;
    private DeviceMemory ComputeImageMemory;
    private ImageView ComputeImageView;

    private Sampler TestSampler;

    private DescriptorSetLayout DescriptorSetLayout;
    private DescriptorPool DescriptorPool;

    private DescriptorSetLayout ComputeDescriptorSetLayout;
    private DescriptorSet ComputeDescriptorSet;

    private CommandPool CommandPool;

    private void CreateResources()
    {
        GraphicsDevice.QueueFamilyIndices queueFamiliyIndicies = GraphicsDevice.FindQueueFamilies(GraphicsDevice.PhysicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamiliyIndicies.GraphicsAndComputeFamily!.Value,
        };

        if (VkAPI.API.CreateCommandPool(Device, in poolInfo, null, out CommandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool");
        }

        {
            ImageCreateInfo imageCreateInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                Format = Format.R8G8B8A8Unorm,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.StorageBit,
                Extent = new Extent3D()
                {
                    Width = (uint)Window.FramebufferSize.X,
                    Height = (uint)Window.FramebufferSize.Y,
                    Depth = 1
                },
                ImageType = ImageType.Type2D,
                Samples = SampleCountFlags.Count1Bit,
                MipLevels = 1,
                ArrayLayers = 1,
                InitialLayout = ImageLayout.Undefined,
                Tiling = ImageTiling.Optimal,
            };

            VkAPI.API.CreateImage(Device, in imageCreateInfo, null, out ComputeImage);

            MemoryRequirements memRequirements = new();
            VkAPI.API.GetImageMemoryRequirements(Device, ComputeImage, out memRequirements);

            MemoryAllocateInfo allocateInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex = GraphicsDevice.FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.None),
            };

            fixed (DeviceMemory* imageMemoryPtr = &ComputeImageMemory)
            {
                if (VkAPI.API.AllocateMemory(Device, allocateInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate image memory!");
                }
            }

            VkAPI.API.BindImageMemory(Device, ComputeImage, ComputeImageMemory, 0);

            ImageViewCreateInfo viewCreateInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = ComputeImage,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                SubresourceRange = new ImageSubresourceRange()
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };

            VkAPI.API.CreateImageView(Device, in viewCreateInfo, null, out ComputeImageView);
        }

        SamplerCreateInfo samplerCreateInfo = new SamplerCreateInfo()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = false,
            MaxAnisotropy = 1.0f
        };

        VkAPI.API.CreateSampler(Device, in samplerCreateInfo, null, out TestSampler);

        TransitionImageLayout(ComputeImage, Format.R8G8B8A8Unorm, ImageLayout.Undefined, ImageLayout.General);
    }

    private void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = CommandPool,
            CommandBufferCount = 1,
        };

        CommandBuffer commandBuffer;
        VkAPI.API.AllocateCommandBuffers(Device, &allocInfo, &commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        VkAPI.API.BeginCommandBuffer(commandBuffer, &beginInfo);

        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            }
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;

            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;

            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.ComputeShaderBit;
        }
        else
        {
            throw new ArgumentException("unsupported layout transition!");
        }

        VkAPI.API.CmdPipelineBarrier(
            commandBuffer,
            sourceStage, destinationStage,
            0,
            0, null,
            0, null,
            1, &barrier
        );

        VkAPI.API.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };

        VkAPI.API.QueueSubmit(GraphicsDevice.GraphicsQueue, 1, &submitInfo, new Fence());
        VkAPI.API.QueueWaitIdle(GraphicsDevice.GraphicsQueue);

        VkAPI.API.FreeCommandBuffers(Device, CommandPool, 1, &commandBuffer);
    }

    private void CreateComputePipeline()
    {
        DescriptorSetLayoutBinding imageLayoutBinding = new()
        {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageImage,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.ComputeBit,
        };

        DescriptorSetLayoutBinding* bindings = stackalloc[] { imageLayoutBinding };
        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = bindings,
        };

        if (VkAPI.API.CreateDescriptorSetLayout(Device, &layoutInfo, null, out ComputeDescriptorSetLayout) != Result.Success)
        {
            throw new Exception("failed to create descriptor set layout!");
        }


        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = DescriptorPool,
            DescriptorSetCount = 1,
        };

        fixed (DescriptorSetLayout* ptr = &ComputeDescriptorSetLayout)
        {
            allocInfo.PSetLayouts = ptr;
        }

        Result ress = Result.Success;
        fixed (DescriptorSet* ptr = &ComputeDescriptorSet)
            if ((ress = VkAPI.API.AllocateDescriptorSets(Device, &allocInfo, ptr)) != Result.Success)
            {
                throw new Exception($"Failed to allocate descriptor sets {ress}");
            }

        DescriptorImageInfo imageInfo = new()
        {
            ImageLayout = ImageLayout.General,
            ImageView = ComputeImageView
        };

        WriteDescriptorSet writeDescriptorSet = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = ComputeDescriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageImage,
            DescriptorCount = 1,
            PImageInfo = &imageInfo,
        };

        VkAPI.API.UpdateDescriptorSets(Device, 1, &writeDescriptorSet, 0, null);

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "Compiled");
        byte[] computeShaderCode = File.ReadAllBytes(Path.Combine(path, "Ray", "Test.spirv"));

        ShaderModule computeShaderModule = CreateShaderModule(computeShaderCode);

        PipelineShaderStageCreateInfo computeShaderStageInfo = new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = computeShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("CSMain")
        };

        DescriptorSetLayout* layouts = stackalloc[] { ComputeDescriptorSetLayout };
        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = layouts,
            PushConstantRangeCount = 0,
        };

        if (VkAPI.API.CreatePipelineLayout(Device, in pipelineLayoutInfo, null, out ComputePipelineLayout) != Result.Success)
        {
            throw new Exception("failed to create pipeline layout!");
        }

        ComputePipelineCreateInfo pipelineCreateInfo = new()
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = computeShaderStageInfo,
            Layout = ComputePipelineLayout,
        };

        fixed (Pipeline* ptr = &ComputePipeline)
            if (VkAPI.API.CreateComputePipelines(Device, new PipelineCache(), 1, &pipelineCreateInfo, null, ptr) != Result.Success)
            {
                throw new Exception("Fuck");
            }

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        VkAPI.API.CreateFence(Device, in fenceInfo, null, out ComputeFence);
    }

    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding samplerLayoutBinding = new()
        {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImmutableSamplers = null,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        DescriptorSetLayoutBinding* bindings = stackalloc[] { samplerLayoutBinding };
        DescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = bindings,
        };

        if (VkAPI.API.CreateDescriptorSetLayout(Device, &layoutInfo, null, out DescriptorSetLayout) != Result.Success)
        {
            throw new Exception("failed to create descriptor set layout!");
        }

        DescriptorPoolSize poolSizes = new()
        {
            Type = DescriptorType.StorageImage,
            DescriptorCount = (uint)Frames.Length + 1,
        };

        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSizes,
            MaxSets = (uint)Frames.Length + 1,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
        };

        if (VkAPI.API.CreateDescriptorPool(Device, &poolInfo, null, out DescriptorPool) != Result.Success)
        {
            throw new Exception("Could not create descriptor pool");
        }
    }

    private void CreateDescriptorSets()
    {
        DescriptorSetLayout* layouts = stackalloc DescriptorSetLayout[Frames.Length];
        for (int i = 0; i < Frames.Length; i++)
        {
            layouts[i] = DescriptorSetLayout;
        }
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = DescriptorPool,
            DescriptorSetCount = (uint)Frames.Length,
            PSetLayouts = layouts
        };

        DescriptorSet* sets = stackalloc DescriptorSet[Frames.Length];
        if (VkAPI.API.AllocateDescriptorSets(Device, &allocInfo, sets) != Result.Success)
        {
            throw new Exception("Failed to allocate descriptor sets");
        }

        for (int i = 0; i < Frames.Length; i++)
        {
            DescriptorImageInfo imageInfo = new()
            {
                ImageLayout = ImageLayout.General,
                ImageView = ComputeImageView,
                Sampler = TestSampler,
            };

            WriteDescriptorSet writeDescriptorSet = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = sets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo,
            };

            VkAPI.API.UpdateDescriptorSets(Device, 1, &writeDescriptorSet, 0, null);

            Frames[i].DescriptorSet = sets[i];
        }
    }

    private void CreateSurface()
    {
        if (!VkAPI.API.TryGetInstanceExtension<KhrSurface>(GraphicsDevice.Instance, out KhrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found");
        }

        Surface = Window.VkSurface!.Create<AllocationCallbacks>(GraphicsDevice.Instance.ToHandle(), null).ToSurface();
    }

    private void CreateSwapChain()
    {
        SwapChain = GraphicsDevice.ResourceFactory.CreateSwapChain(Surface);
    }

    private void RecreateSwapChain()
    {
        Vector2D<int> framebufferSize = Window.FramebufferSize;

        while (framebufferSize.X == 0 || framebufferSize.Y == 0)
        {
            framebufferSize = Window.FramebufferSize;
            Window.DoEvents();
        }

        VkAPI.API.DeviceWaitIdle(Device);

        CleanUpSwapChain();

        CreateSwapChain();
        CreateImageViews();
        CreateRenderPass();
        CreateDescriptorSets();
        CreateGraphicsPipeline();
        CreateFramebuffers();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateFences();
    }

    private void CleanUpSwapChain()
    {
        foreach (FrameData frameData in Frames)
        {
            VkAPI.API.DestroyFramebuffer(Device, frameData.Framebuffer, null);
            VkAPI.API.FreeCommandBuffers(Device, frameData.CommandPool, 1, &frameData.CommandBuffer);
            VkAPI.API.DestroyCommandPool(Device, frameData.CommandPool, null);
            VkAPI.API.DestroyImageView(Device, frameData.SwapChainImageView, null);
            VkAPI.API.DestroyFence(Device, frameData.Fence, null);
            VkAPI.API.FreeDescriptorSets(Device, DescriptorPool, 1, &frameData.DescriptorSet);
        }

        VkAPI.API.DestroyPipeline(Device, graphicsPipeline, null);
        VkAPI.API.DestroyPipelineLayout(Device, pipelineLayout, null);
        VkAPI.API.DestroyRenderPass(Device, renderPass, null);

        KhrSwapChain!.DestroySwapchain(Device, SwapChain, null);
    }

    private void CreateImageViews()
    {
        for (int i = 0; i < Frames.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = Frames[i].SwapChainImage,
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

            if (VkAPI.API.CreateImageView(Device, in createInfo, null, out Frames[i].SwapChainImageView) != Result.Success)
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

        VertexInputAttributeDescription* vertexAttribtues = stackalloc[]
        {
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.pos)),
            },
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32Sfloat,
                Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.uv)),
            }
        };

        VertexInputBindingDescription* bindingDescription = stackalloc[]
        {
            new VertexInputBindingDescription()
            {
                Binding = 0,
                Stride = (uint)Unsafe.SizeOf<Vertex>(),
                InputRate = VertexInputRate.Vertex,
            }
        };

        PipelineVertexInputStateCreateInfo vertexInputInfo = new()
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            VertexAttributeDescriptionCount = 2,
            PVertexAttributeDescriptions = vertexAttribtues,
            PVertexBindingDescriptions = bindingDescription
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

        DescriptorSetLayout* layouts = stackalloc[] { DescriptorSetLayout };
        PipelineLayoutCreateInfo pipelineLayoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = layouts,
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
        for (int i = 0; i < Frames.Length; i++)
        {
            ImageView attachment = Frames[i].SwapChainImageView;

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

            if (VkAPI.API.CreateFramebuffer(Device, in framebufferInfo, null, out Frames[i].Framebuffer) != Result.Success)
            {
                throw new Exception("Failed to create Framebuffer");
            }
        }
    }

    private void CreateCommandPool()
    {
        GraphicsDevice.QueueFamilyIndices queueFamiliyIndicies = GraphicsDevice.FindQueueFamilies(GraphicsDevice.PhysicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamiliyIndicies.GraphicsAndComputeFamily!.Value,
        };

        for (int i = 0; i < Frames.Length; i++)
        {
            if (VkAPI.API.CreateCommandPool(Device, in poolInfo, null, out Frames[i].CommandPool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }

        if (VkAPI.API.CreateCommandPool(Device, in poolInfo, null, out ComputeCommandPool) != Result.Success)
        {
            throw new Exception("Failed to create command pool");
        }
    }

    private void CreateCommandBuffers()
    {
        for (int i = 0; i < Frames.Length; i++)
        {
            CommandBufferAllocateInfo allocInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = Frames[i].CommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };

            fixed (CommandBuffer* commandBuffersPtr = &Frames[i].CommandBuffer)
            {
                if (VkAPI.API.AllocateCommandBuffers(Device, in allocInfo, commandBuffersPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate command buffers");
                }
            }
        }

        {
            CommandBufferAllocateInfo allocInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = ComputeCommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };

            fixed (CommandBuffer* commandBuffersPtr = &ComputeCommandBuffer)
            {
                if (VkAPI.API.AllocateCommandBuffers(Device, in allocInfo, commandBuffersPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate command buffers");
                }
            }
        }
    }

    private void CreateSemaphores()
    {
        Semaphores = new FrameSemaphores[Frames.Length + 1];

        SemaphoreCreateInfo semaphoreInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        for (int i = 0; i < Frames.Length + 1; i++)
        {
            if (VkAPI.API.CreateSemaphore(Device, in semaphoreInfo, null, out Semaphores[i].ImageAcquiredSemaphore) != Result.Success ||
                VkAPI.API.CreateSemaphore(Device, in semaphoreInfo, null, out Semaphores[i].RenderCompleteSemaphore) != Result.Success)
            {
                throw new Exception("Failed to create Semaphore for frame");
            }
        }
    }

    private void CreateFences()
    {
        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (int i = 0; i < Frames.Length; i++)
        {
            if (VkAPI.API.CreateFence(Device, in fenceInfo, null, out Frames[i].Fence) != Result.Success)
            {
                throw new Exception("Failed to create Fence for frame");
            }
        }
    }

    public void Render(float delta)
    {
        FrameSemaphores frameSemaphores = Semaphores[SemaphoreIndex];
        Result result = KhrSwapChain!.AcquireNextImage(Device, SwapChain, ulong.MaxValue, frameSemaphores.ImageAcquiredSemaphore, default, ref FrameIndex);

        if (result == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapChain();
            return;
        }
        else if (result != Result.Success && result != Result.SuboptimalKhr)
        {
            throw new Exception("failed to acquire swap chain image!");
        }

        ref FrameData frame = ref Frames[FrameIndex];

        if (frame.Fence.Handle != default)
        {
            VkAPI.API.WaitForFences(Device, 1, frame.Fence, true, ulong.MaxValue);
        }
        else
        {
            FenceCreateInfo fenceInfo = new()
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit,
            };

            if (VkAPI.API.CreateFence(Device, in fenceInfo, null, out frame.Fence) != Result.Success)
            {
                throw new Exception("Failed to create Fence for frame");
            }
        }
        VkAPI.API.ResetFences(Device, 1, frame.Fence);


        VkAPI.API.ResetFences(Device, 1, ComputeFence);
        ComputeFrame(delta);
        VkAPI.API.WaitForFences(Device, 1, ComputeFence, true, ulong.MaxValue);

        DrawFrame(delta);

        uint frameIndex = FrameIndex;
        SwapchainKHR* swapChains = stackalloc[] { SwapChain };
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,

            WaitSemaphoreCount = 1,
            PWaitSemaphores = &frameSemaphores.RenderCompleteSemaphore,

            SwapchainCount = 1,
            PSwapchains = swapChains,

            PImageIndices = &frameIndex
        };

        result = KhrSwapChain.QueuePresent(GraphicsDevice.PresentQueue, presentInfo);

        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
        {
            //frameBufferResized = false;
            RecreateSwapChain();
        }
        else if (result != Result.Success)
        {
            throw new Exception("failed to present swap chain image!");
        }

        SemaphoreIndex = (uint)((SemaphoreIndex + 1) % Semaphores.Length);
    }

    private void ComputeFrame(float delta)
    {
        VkAPI.API.ResetCommandPool(Device, ComputeCommandPool, CommandPoolResetFlags.None);

        CommandBufferBeginInfo commandBufferBeginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VkAPI.API.BeginCommandBuffer(ComputeCommandBuffer, in commandBufferBeginInfo);

        VkAPI.API.CmdBindDescriptorSets(ComputeCommandBuffer, PipelineBindPoint.Compute, ComputePipelineLayout, 0, 1, ComputeDescriptorSet, 0, 0);

        VkAPI.API.CmdBindPipeline(ComputeCommandBuffer, PipelineBindPoint.Compute, ComputePipeline);
        VkAPI.API.CmdDispatch(ComputeCommandBuffer, (uint)MathF.Ceiling(1920f / 32f), (uint)MathF.Ceiling(1080f / 32f), 1);

        if (VkAPI.API.EndCommandBuffer(ComputeCommandBuffer) != Result.Success)
        {
            throw new Exception("failed to record command buffer!");
        }

        CommandBuffer buffer = ComputeCommandBuffer;
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 0,
            CommandBufferCount = 1,
            PCommandBuffers = &buffer,
        };

        if (VkAPI.API.QueueSubmit(GraphicsDevice.ComputeQueue, 1, submitInfo, ComputeFence) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer");
        }
    }

    private void DrawFrame(float delta)
    {
        ref FrameData frame = ref Frames[FrameIndex];
        FrameSemaphores frameSemaphores = Semaphores[SemaphoreIndex];
        Logger.LogInformation($"Guh: {1 / delta}");
        
        // --- Begin ---
        VkAPI.API.ResetCommandPool(Device, frame.CommandPool, CommandPoolResetFlags.None);

        CommandBufferBeginInfo commandBufferBeginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VkAPI.API.BeginCommandBuffer(frame.CommandBuffer, in commandBufferBeginInfo);

        RenderPassBeginInfo renderPassInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = frame.Framebuffer,
            RenderArea =
            {
                Offset = { X = 0, Y = 0 },
                Extent = SwapChainExtent,
            },
            ClearValueCount = 1,
        };

        ClearValue clearColor = new()
        {
            Color = new() { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 1f },
        };

        renderPassInfo.PClearValues = &clearColor;

        // --- Draw ---
        VkAPI.API.CmdBindPipeline(frame.CommandBuffer, PipelineBindPoint.Graphics, graphicsPipeline);
        VkAPI.API.CmdBeginRenderPass(frame.CommandBuffer, in renderPassInfo, SubpassContents.Inline);

        Buffer* vertexBuffers = stackalloc[] { vertexBuffer };
        ulong* offsets = stackalloc ulong[] { 0 };
        VkAPI.API.CmdBindVertexBuffers(frame.CommandBuffer, 0, 1, vertexBuffers, offsets);

        fixed (DescriptorSet* descriptorSetPtr = &frame.DescriptorSet)
            VkAPI.API.CmdBindDescriptorSets(frame.CommandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSetPtr, 0, null);

        VkAPI.API.CmdDraw(frame.CommandBuffer, (uint)vertices.Length, 1, 0, 0);

        VkAPI.API.CmdEndRenderPass(frame.CommandBuffer);

        if (VkAPI.API.EndCommandBuffer(frame.CommandBuffer) != Result.Success)
        {
            throw new Exception("failed to record command buffer!");
        }

        Semaphore* waitSemaphores = stackalloc[] { frameSemaphores.ImageAcquiredSemaphore };
        PipelineStageFlags* waitStages = stackalloc[] { PipelineStageFlags.ColorAttachmentOutputBit };
        Semaphore* signalSemaphores = stackalloc[] { frameSemaphores.RenderCompleteSemaphore };

        CommandBuffer buffer = frame.CommandBuffer;

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,

            CommandBufferCount = 1,
            PCommandBuffers = &buffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores,
        };

        if (VkAPI.API.QueueSubmit(GraphicsDevice.GraphicsQueue, 1, submitInfo, frame.Fence) != Result.Success)
        {
            throw new Exception("Failed to submit draw command buffer");
        }
    }

    private void CreateVertexBuffer()
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)(sizeof(Vertex) * vertices.Length),
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive,
        };

        fixed (Buffer* vertexBufferPtr = &vertexBuffer)
        {
            if (VkAPI.API.CreateBuffer(Device, bufferInfo, null, vertexBufferPtr) != Result.Success)
            {
                throw new Exception("failed to create vertex buffer!");
            }
        }

        MemoryRequirements memRequirements = new();
        VkAPI.API.GetBufferMemoryRequirements(Device, vertexBuffer, out memRequirements);

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = GraphicsDevice.FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };

        fixed (DeviceMemory* vertexBufferMemoryPtr = &vertexBufferMemory)
        {
            if (VkAPI.API.AllocateMemory(Device, allocateInfo, null, vertexBufferMemoryPtr) != Result.Success)
            {
                throw new Exception("failed to allocate vertex buffer memory!");
            }
        }

        VkAPI.API.BindBufferMemory(Device, vertexBuffer, vertexBufferMemory, 0);

        void* data;
        VkAPI.API.MapMemory(Device, vertexBufferMemory, 0, bufferInfo.Size, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        VkAPI.API.UnmapMemory(Device, vertexBufferMemory);
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

   
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            VkAPI.API.DeviceWaitIdle(Device);

            CleanUpSwapChain();

            foreach (FrameSemaphores semaphores in Semaphores)
            {
                VkAPI.API.DestroySemaphore(Device, semaphores.ImageAcquiredSemaphore, null);
                VkAPI.API.DestroySemaphore(Device, semaphores.RenderCompleteSemaphore, null);
            }

            VkAPI.API.DestroyBuffer(Device, vertexBuffer, null);
            VkAPI.API.FreeMemory(Device, vertexBufferMemory, null);

            GraphicsDevice.Dispose();

            KhrSurface?.DestroySurface(GraphicsDevice.Instance, Surface, null);
            VkAPI.API.DestroyInstance(GraphicsDevice.Instance, null);

            _disposed = true;
        }
    }

    record struct FrameData
    {
        public CommandPool CommandPool;
        public CommandBuffer CommandBuffer;
        public Fence Fence;
        public Image SwapChainImage;
        public ImageView SwapChainImageView;
        public Framebuffer Framebuffer;
        public DescriptorSet DescriptorSet;
    }

    record struct FrameSemaphores
    {
        public Semaphore ImageAcquiredSemaphore;
        public Semaphore RenderCompleteSemaphore;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
