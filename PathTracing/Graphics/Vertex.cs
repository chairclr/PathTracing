using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace PathTracing.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 pos;
    public Vector2 uv;

    public static VertexInputBindingDescription GetBindingDescription()
    {
        VertexInputBindingDescription bindingDescription = new()
        {
            Binding = 0,
            Stride = (uint)Unsafe.SizeOf<Vertex>(),
            InputRate = VertexInputRate.Vertex,
        };

        return bindingDescription;
    }

    public static unsafe VertexInputAttributeDescription* GetVertexAttributeDescriptions()
    {
        VertexInputAttributeDescription* attributeDescriptions = stackalloc[]
        {
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(pos)),
            },
            new VertexInputAttributeDescription()
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32Sfloat,
                Offset = (uint)Marshal.OffsetOf<Vertex>(nameof(pos)),
            }
        };

        return attributeDescriptions;
    }
}

