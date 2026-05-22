using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using Silk.NET.Vulkan;

using System.Runtime.InteropServices;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendBuffers(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : LegacyCinematicVulkanBackendResources(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
{
    protected void UpdateDescriptorSet(
        DescriptorSet descriptorSet,
        PreparedDraw preparedDraw,
        FrameOutput output,
        GpuBuffer drawParamBuffer,
        GpuBuffer triangleBuffer)
    {
        DescriptorImageInfo outputInfo = new()
        {
            ImageLayout = ImageLayout.General,
            ImageView = output.ImageView
        };
        DescriptorBufferInfo drawParamsInfo = new()
        {
            Buffer = drawParamBuffer.Buffer,
            Offset = 0,
            Range = drawParamBuffer.Size
        };
        DescriptorBufferInfo triangleInfo = new()
        {
            Buffer = triangleBuffer.Buffer,
            Offset = 0,
            Range = triangleBuffer.Size
        };
        DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[MaxMaterialLayers];
        for (int i = 0; i < MaxMaterialLayers; i++)
        {
            imageInfos[i] = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = preparedDraw.Textures[i].ImageView,
                Sampler = nearestSampler
            };
        }

        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[4];
        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageImage,
            PImageInfo = &outputInfo
        };
        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 1,
            DescriptorCount = MaxMaterialLayers,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = imageInfos
        };
        writes[2] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 2,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &drawParamsInfo
        };
        writes[3] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 3,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &triangleInfo
        };
        vk.UpdateDescriptorSets(device, 4, writes, 0, null);
    }

    protected void EnsureFrameOutput(int width, int height)
    {
        ulong byteLength = checked((ulong)width * (ulong)height * 4UL);
        if (frameOutput != null && frameOutput.Width == width && frameOutput.Height == height)
            return;

        frameOutput?.Dispose();
        CreateImage(
            width,
            height,
            Format.R32Uint,
            ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            out VkImage image,
            out DeviceMemory imageMemory);
        ImageView imageView = default;
        GpuBuffer? readbackBuffer = null;
        try
        {
            imageView = CreateImageView(image, Format.R32Uint);
            readbackBuffer = CreateBufferResource(
                byteLength,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            frameOutput = new FrameOutput(vk, device, image, imageMemory, imageView, readbackBuffer, width, height);
        }
        catch
        {
            readbackBuffer?.Dispose();
            if (imageView.Handle != 0)
                vk.DestroyImageView(device, imageView, null);
            vk.DestroyImage(device, image, null);
            vk.FreeMemory(device, imageMemory, null);
            throw;
        }
    }

    protected GpuBuffer CreateAndUploadBuffer(ReadOnlySpan<byte> data, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        GpuBuffer buffer = CreateBufferResource((ulong)data.Length, usage, properties);
        void* mapped = null;
        ThrowIfFailed(vk.MapMemory(device, buffer.Memory, 0, buffer.Size, 0, &mapped), "vkMapMemory(upload buffer)");
        try
        {
            data.CopyTo(new Span<byte>(mapped, data.Length));
        }
        finally
        {
            vk.UnmapMemory(device, buffer.Memory);
        }

        return buffer;
    }

    protected GpuBuffer CreateBufferResource(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        CreateBuffer(size, usage, properties, fallbackProperties: null, out VkBuffer buffer, out DeviceMemory memory);
        return new GpuBuffer(vk, device, buffer, memory, size);
    }

    protected GpuBuffer CreateBufferResource(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags properties,
        MemoryPropertyFlags fallbackProperties)
    {
        CreateBuffer(size, usage, properties, fallbackProperties, out VkBuffer buffer, out DeviceMemory memory);
        return new GpuBuffer(vk, device, buffer, memory, size);
    }

    protected void CopyReadbackBufferToCanvas(CanvasBuffer outputCanvas, GpuBuffer output)
    {
        int byteLength = checked(outputCanvas.Width * outputCanvas.Height * 4);
        InvalidateMappedMemory(output);
        void* mapped = null;
        ThrowIfFailed(vk.MapMemory(device, output.Memory, 0, output.Size, 0, &mapped), "vkMapMemory(output)");
        try
        {
            ReadOnlySpan<byte> source = new(mapped, byteLength);
            Span<byte> target = MemoryMarshal.AsBytes(outputCanvas.Pixels.AsSpan());
            source.CopyTo(target);
        }
        finally
        {
            vk.UnmapMemory(device, output.Memory);
        }
    }

    protected void CopyReadbackBufferToBuffer(byte[] outputBuffer, int byteLength, GpuBuffer output)
    {
        if (outputBuffer.Length < byteLength)
            throw new ArgumentException("Raw output buffer is smaller than the frame.", nameof(outputBuffer));

        InvalidateMappedMemory(output);
        void* mapped = null;
        ThrowIfFailed(vk.MapMemory(device, output.Memory, 0, output.Size, 0, &mapped), "vkMapMemory(output)");
        try
        {
            ReadOnlySpan<byte> source = new(mapped, byteLength);
            source.CopyTo(outputBuffer);
        }
        finally
        {
            vk.UnmapMemory(device, output.Memory);
        }
    }

    protected void InvalidateMappedMemory(GpuBuffer output)
    {
        MappedMemoryRange range = new()
        {
            SType = StructureType.MappedMemoryRange,
            Memory = output.Memory,
            Offset = 0,
            Size = ulong.MaxValue
        };
        ThrowIfFailed(vk.InvalidateMappedMemoryRanges(device, 1, &range), "vkInvalidateMappedMemoryRanges(output)");
    }
}