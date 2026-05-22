using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendBase(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : IDisposable
{
    protected const int MaxMaterialLayers = 4;
    protected const int MaxFrameTextures = 512;
    protected const int DrawModeQuad = 0;
    protected const int DrawModeTriangle = 1;
    protected const int DrawModeMesh = 2;

    protected readonly object renderLock = new();
    protected readonly Vk vk = vk;
    protected readonly Instance instance = instance;
    protected readonly PhysicalDevice physicalDevice = physicalDevice;
    protected readonly Device device = device;
    protected readonly Queue graphicsQueue = graphicsQueue;
    protected readonly uint graphicsQueueFamilyIndex = graphicsQueueFamilyIndex;
    protected readonly CommandPool commandPool = commandPool;
    protected readonly Dictionary<object, VulkanTexture> textureCache = new(ReferenceEqualityComparer.Instance);
    protected readonly Dictionary<string, VulkanTexture> transientTextureCache = new(StringComparer.Ordinal);

    protected DescriptorSetLayout descriptorSetLayout;
    protected PipelineLayout pipelineLayout;
    protected Pipeline computePipeline;
    protected Sampler nearestSampler;
    protected DescriptorPool descriptorPool;
    protected int descriptorPoolCapacity;
    protected FrameOutput? frameOutput;
    protected GpuBuffer? emptyTriangleBuffer;
    protected VulkanTexture? whiteTexture;
    protected bool disposed;

    public string DeviceName { get; } = deviceName;

    public abstract void Dispose();

    [StructLayout(LayoutKind.Sequential)]
    protected readonly record struct GpuInt4(int X, int Y, int Z, int W);

    [StructLayout(LayoutKind.Sequential)]
    protected struct GpuLayerParams
    {
        public static GpuLayerParams Disabled => new()
        {
            UvRow0 = new Vector4(1, 0, 0, 0),
            UvRow1 = new Vector4(0, 1, 0, 0),
            Diffuse = Vector4.One,
            Params0 = new GpuInt4(0, 2, (int)LegacyCinematicTextureUsage.EntireTexture, (int)TextureAddressMode.Wrap),
            Params1 = new GpuInt4((int)TextureAddressMode.Wrap, 0, 0, 0)
        };

        public Vector4 UvRow0;
        public Vector4 UvRow1;
        public Vector4 Diffuse;
        public GpuInt4 Params0;
        public GpuInt4 Params1;
    }

    [StructLayout(LayoutKind.Sequential)]
    protected struct GpuDrawParams
    {
        public Vector4 OriginAxisU;
        public Vector4 AxisVDetAlpha;
        public Vector4 TintMaxY;
        public Vector4 Triangle0;
        public Vector4 Triangle1;
        public Vector4 Triangle2;
        public Vector4 UvOverride;
        public GpuInt4 OutputRect;
        public GpuInt4 RectBlendMode;
        public GpuInt4 MeshOffsetCount;
        public GpuLayerParams Layer0;
        public GpuLayerParams Layer1;
        public GpuLayerParams Layer2;
        public GpuLayerParams Layer3;
    }

    [StructLayout(LayoutKind.Sequential)]
    protected struct GpuPushConstants
    {
        public Vector4 TintMaxY;
        public int DrawIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    protected struct GpuTriangleParams
    {
        public Vector4 Triangle0;
        public Vector4 Triangle1;
        public Vector4 Triangle2;
    }

    protected sealed class GpuBuffer(Vk vk, Device device, VkBuffer buffer, DeviceMemory memory, ulong size) : IDisposable
    {
        public VkBuffer Buffer { get; } = buffer;
        public DeviceMemory Memory { get; } = memory;
        public ulong Size { get; } = size;

        public void Dispose()
        {
            vk.DestroyBuffer(device, Buffer, null);
            vk.FreeMemory(device, Memory, null);
        }
    }

    protected sealed class FrameOutput(
        Vk vk,
        Device device,
        VkImage image,
        DeviceMemory memory,
        ImageView imageView,
        GpuBuffer readbackBuffer,
        int width,
        int height) : IDisposable
    {
        public VkImage Image { get; } = image;
        public ImageView ImageView { get; } = imageView;
        public GpuBuffer ReadbackBuffer { get; } = readbackBuffer;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public ImageLayout Layout { get; set; } = ImageLayout.Undefined;
        public AccessFlags AccessMask { get; set; } = AccessFlags.None;
        public PipelineStageFlags StageMask { get; set; } = PipelineStageFlags.TopOfPipeBit;

        public void Dispose()
        {
            ReadbackBuffer.Dispose();
            vk.DestroyImageView(device, ImageView, null);
            vk.DestroyImage(device, Image, null);
            vk.FreeMemory(device, memory, null);
        }
    }

    protected sealed class VulkanTexture(
        Vk vk,
        Device device,
        VkImage image,
        DeviceMemory memory,
        ImageView imageView,
        string name,
        int byteLength,
        int width,
        int height) : IDisposable
    {
        public VkImage Image { get; } = image;
        public ImageView ImageView { get; } = imageView;
        public string Name { get; } = name;
        public int ByteLength { get; } = byteLength;
        public int Width { get; } = width;
        public int Height { get; } = height;

        public void Dispose()
        {
            vk.DestroyImageView(device, ImageView, null);
            vk.DestroyImage(device, Image, null);
            vk.FreeMemory(device, memory, null);
        }
    }

    protected sealed class PreparedDraw(
        GpuDrawParams parameters,
        VulkanTexture[] textures,
        List<VulkanTexture>? transientTextures)
    {
        public GpuDrawParams Params { get; } = parameters;
        public VulkanTexture[] Textures { get; } = textures;

        public void DisposeTransientTextures()
        {
            if (transientTextures == null)
                return;

            foreach (VulkanTexture texture in transientTextures)
                texture.Dispose();
        }
    }

    protected sealed class PreparedFrame(
        GpuDrawParams[] drawParams,
        GpuTriangleParams[] triangles,
        VulkanTexture[] textures,
        List<VulkanTexture>? transientTextures)
    {
        public GpuDrawParams[] DrawParams { get; } = drawParams;
        public GpuTriangleParams[] Triangles { get; } = triangles;
        public VulkanTexture[] Textures { get; } = textures;

        public void DisposeTransientTextures()
        {
            if (transientTextures == null)
                return;

            foreach (VulkanTexture texture in transientTextures)
                texture.Dispose();
        }
    }
}