using Silk.NET.Vulkan;

using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendResources(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : LegacyCinematicVulkanBackendBase(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
{
    protected void CreateImage(
        int width,
        int height,
        Format format,
        ImageUsageFlags usage,
        out VkImage image,
        out DeviceMemory memory)
    {
        ImageCreateInfo createInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        ThrowIfFailed(vk.CreateImage(device, &createInfo, null, out image), "vkCreateImage");

        MemoryRequirements memoryRequirements;
        vk.GetImageMemoryRequirements(device, image, &memoryRequirements);
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        ThrowIfFailed(vk.AllocateMemory(device, &allocateInfo, null, out memory), "vkAllocateMemory(image)");
        ThrowIfFailed(vk.BindImageMemory(device, image, memory, 0), "vkBindImageMemory");
    }

    protected void CreateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags properties,
        out VkBuffer buffer,
        out DeviceMemory memory) =>
        CreateBuffer(size, usage, properties, fallbackProperties: null, out buffer, out memory);

    protected void CreateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags properties,
        MemoryPropertyFlags? fallbackProperties,
        out VkBuffer buffer,
        out DeviceMemory memory)
    {
        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        ThrowIfFailed(vk.CreateBuffer(device, &createInfo, null, out buffer), "vkCreateBuffer");

        MemoryRequirements memoryRequirements;
        vk.GetBufferMemoryRequirements(device, buffer, &memoryRequirements);
        if (!TryFindMemoryType(memoryRequirements.MemoryTypeBits, properties, out uint memoryTypeIndex))
        {
            if (fallbackProperties == null ||
                !TryFindMemoryType(memoryRequirements.MemoryTypeBits, fallbackProperties.Value, out memoryTypeIndex))
            {
                string suffix = fallbackProperties == null
                    ? string.Empty
                    : $" or fallback {fallbackProperties.Value}";
                throw new InvalidOperationException($"Could not find Vulkan memory type for {properties}{suffix}.");
            }
        }

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = memoryTypeIndex
        };
        ThrowIfFailed(vk.AllocateMemory(device, &allocateInfo, null, out memory), "vkAllocateMemory(buffer)");
        ThrowIfFailed(vk.BindBufferMemory(device, buffer, memory, 0), "vkBindBufferMemory");
    }

    protected uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        if (TryFindMemoryType(typeFilter, properties, out uint memoryTypeIndex))
            return memoryTypeIndex;

        throw new InvalidOperationException($"Could not find Vulkan memory type for {properties}.");
    }

    protected bool TryFindMemoryType(uint typeFilter, MemoryPropertyFlags properties, out uint memoryTypeIndex)
    {
        PhysicalDeviceMemoryProperties memoryProperties;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, &memoryProperties);
        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                memoryTypeIndex = i;
                return true;
            }
        }

        memoryTypeIndex = 0;
        return false;
    }

    protected CommandBuffer BeginOneTimeCommands()
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        ThrowIfFailed(vk.AllocateCommandBuffers(device, &allocateInfo, out CommandBuffer commandBuffer), "vkAllocateCommandBuffers");

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        ThrowIfFailed(vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        return commandBuffer;
    }

    protected void EndOneTimeCommands(CommandBuffer commandBuffer)
    {
        ThrowIfFailed(vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        ThrowIfFailed(vk.QueueSubmit(graphicsQueue, 1, &submitInfo, default), "vkQueueSubmit");
        ThrowIfFailed(vk.QueueWaitIdle(graphicsQueue), "vkQueueWaitIdle");
        vk.FreeCommandBuffers(device, commandPool, 1, &commandBuffer);
    }

    protected void TransitionImageLayout(
        CommandBuffer commandBuffer,
        VkImage image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags srcAccessMask,
        AccessFlags dstAccessMask,
        PipelineStageFlags srcStageMask,
        PipelineStageFlags dstStageMask,
        ImageSubresourceRange subresourceRange)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = subresourceRange,
            SrcAccessMask = srcAccessMask,
            DstAccessMask = dstAccessMask
        };
        vk.CmdPipelineBarrier(commandBuffer, srcStageMask, dstStageMask, 0, 0, null, 0, null, 1, &barrier);
    }

    protected void TransitionOutputImage(
        CommandBuffer commandBuffer,
        FrameOutput output,
        ImageLayout newLayout,
        AccessFlags dstAccessMask,
        PipelineStageFlags dstStageMask)
    {
        ImageBarrier(
            commandBuffer,
            output.Image,
            output.Layout,
            newLayout,
            output.AccessMask,
            dstAccessMask,
            output.StageMask,
            dstStageMask,
            CreateColorRange());
        output.Layout = newLayout;
        output.AccessMask = dstAccessMask;
        output.StageMask = dstStageMask;
    }

    protected void ImageBarrier(
        CommandBuffer commandBuffer,
        VkImage image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        AccessFlags srcAccessMask,
        AccessFlags dstAccessMask,
        PipelineStageFlags srcStageMask,
        PipelineStageFlags dstStageMask,
        ImageSubresourceRange subresourceRange)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = subresourceRange,
            SrcAccessMask = srcAccessMask,
            DstAccessMask = dstAccessMask
        };
        vk.CmdPipelineBarrier(commandBuffer, srcStageMask, dstStageMask, 0, 0, null, 0, null, 1, &barrier);
    }

    protected void BufferBarrier(
        CommandBuffer commandBuffer,
        VkBuffer buffer,
        ulong size,
        AccessFlags srcAccessMask,
        AccessFlags dstAccessMask,
        PipelineStageFlags srcStageMask,
        PipelineStageFlags dstStageMask)
    {
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = srcAccessMask,
            DstAccessMask = dstAccessMask,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = buffer,
            Offset = 0,
            Size = size
        };
        vk.CmdPipelineBarrier(commandBuffer, srcStageMask, dstStageMask, 0, 0, null, 1, &barrier, 0, null);
    }

    protected ImageView CreateImageView(VkImage image, Format format)
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = CreateColorRange()
        };
        ThrowIfFailed(vk.CreateImageView(device, &createInfo, null, out ImageView imageView), "vkCreateImageView");
        return imageView;
    }

    protected static ImageSubresourceRange CreateColorRange() =>
        new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

    protected static Bgra32 CreateOpaqueWhitePixel()
    {
        Bgra32 pixel = default;
        pixel.R = 255;
        pixel.G = 255;
        pixel.B = 255;
        pixel.A = 255;
        return pixel;
    }

    protected static string PtrToString(byte* value)
    {
        int length = 0;
        while (value[length] != 0)
            length++;

        return Encoding.UTF8.GetString(value, length);
    }

    protected static void ThrowIfFailed(Result result, string operation)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}