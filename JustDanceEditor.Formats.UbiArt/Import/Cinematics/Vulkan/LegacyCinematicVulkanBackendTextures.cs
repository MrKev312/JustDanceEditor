using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using Silk.NET.Vulkan;

using System.Globalization;
using System.Runtime.InteropServices;

using VkImage = Silk.NET.Vulkan.Image;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendTextures(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : LegacyCinematicVulkanBackendBuffers(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
{
    protected VulkanTexture GetOrCreateTexture(LegacyCinematicGpuTextureSource source)
    {
        if (source.Cacheable)
        {
            if (textureCache.TryGetValue(source.CacheKey, out VulkanTexture? cached))
                return cached;

            VulkanTexture texture = CreateTexture(source);
            textureCache.Add(source.CacheKey, texture);
            return texture;
        }

        string transientKey = CreateTransientTextureKey(source);
        if (transientTextureCache.TryGetValue(transientKey, out VulkanTexture? reusableTexture))
        {
            UpdateTexture(reusableTexture, source);
            return reusableTexture;
        }

        VulkanTexture transientTexture = CreateTexture(source);
        transientTextureCache.Add(transientKey, transientTexture);
        return transientTexture;
    }

    protected static string CreateTransientTextureKey(LegacyCinematicGpuTextureSource source) =>
        string.Create(CultureInfo.InvariantCulture, $"{source.Name}:{source.Width}x{source.Height}");

    protected VulkanTexture CreateTexture(LegacyCinematicGpuTextureSource source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidOperationException($"Cannot upload empty Vulkan texture {source.Name}.");

        int byteLength = checked(source.Width * source.Height * 4);
        using GpuBuffer stagingBuffer = CreateAndUploadBuffer(
            MemoryMarshal.AsBytes(source.Pixels.AsSpan(0, checked(source.Width * source.Height))),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        CreateImage(
            source.Width,
            source.Height,
            Format.B8G8R8A8Unorm,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            out VkImage image,
            out DeviceMemory imageMemory);
        ImageView imageView = default;
        try
        {
            CommandBuffer commandBuffer = BeginOneTimeCommands();
            ImageSubresourceRange colorRange = CreateColorRange();
            TransitionImageLayout(
                commandBuffer,
                image,
                ImageLayout.Undefined,
                ImageLayout.TransferDstOptimal,
                AccessFlags.None,
                AccessFlags.TransferWriteBit,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                colorRange);

            BufferImageCopy copyRegion = new()
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D((uint)source.Width, (uint)source.Height, 1)
            };
            vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer.Buffer, image, ImageLayout.TransferDstOptimal, 1, &copyRegion);

            TransitionImageLayout(
                commandBuffer,
                image,
                ImageLayout.TransferDstOptimal,
                ImageLayout.ShaderReadOnlyOptimal,
                AccessFlags.TransferWriteBit,
                AccessFlags.ShaderReadBit,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.ComputeShaderBit,
                colorRange);
            EndOneTimeCommands(commandBuffer);

            imageView = CreateImageView(image, Format.B8G8R8A8Unorm);
            return new VulkanTexture(vk, device, image, imageMemory, imageView, source.Name, byteLength, source.Width, source.Height);
        }
        catch
        {
            if (imageView.Handle != 0)
                vk.DestroyImageView(device, imageView, null);
            vk.DestroyImage(device, image, null);
            vk.FreeMemory(device, imageMemory, null);
            throw;
        }
    }

    protected void UpdateTexture(VulkanTexture texture, LegacyCinematicGpuTextureSource source)
    {
        if (texture.Width != source.Width || texture.Height != source.Height)
            throw new InvalidOperationException($"Cannot update Vulkan texture {texture.Name} with differently sized source {source.Name}.");

        using GpuBuffer stagingBuffer = CreateAndUploadBuffer(
            MemoryMarshal.AsBytes(source.Pixels.AsSpan(0, checked(source.Width * source.Height))),
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        CommandBuffer commandBuffer = BeginOneTimeCommands();
        ImageSubresourceRange colorRange = CreateColorRange();
        TransitionImageLayout(
            commandBuffer,
            texture.Image,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.TransferDstOptimal,
            AccessFlags.ShaderReadBit,
            AccessFlags.TransferWriteBit,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.TransferBit,
            colorRange);

        BufferImageCopy copyRegion = new()
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D((uint)source.Width, (uint)source.Height, 1)
        };
        vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer.Buffer, texture.Image, ImageLayout.TransferDstOptimal, 1, &copyRegion);

        TransitionImageLayout(
            commandBuffer,
            texture.Image,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.TransferWriteBit,
            AccessFlags.ShaderReadBit,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.ComputeShaderBit,
            colorRange);
        EndOneTimeCommands(commandBuffer);
    }

    protected DescriptorSet AllocateAndUpdateFrameDescriptorSet(
        PreparedFrame preparedFrame,
        FrameOutput output,
        GpuBuffer drawParamBuffer,
        GpuBuffer triangleBuffer)
    {
        EnsureDescriptorPool(1);
        DescriptorSetLayout layout = descriptorSetLayout;
        DescriptorSet descriptorSet;
        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };
        ThrowIfFailed(vk.AllocateDescriptorSets(device, &allocateInfo, &descriptorSet), "vkAllocateDescriptorSets(frame)");
        UpdateFrameDescriptorSet(descriptorSet, preparedFrame, output, drawParamBuffer, triangleBuffer);
        return descriptorSet;
    }

    protected void UpdateFrameDescriptorSet(
        DescriptorSet descriptorSet,
        PreparedFrame preparedFrame,
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
        DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[MaxFrameTextures];
        VulkanTexture fallbackTexture = whiteTexture ?? throw new InvalidOperationException("White Vulkan texture was not created.");
        for (int i = 0; i < MaxFrameTextures; i++)
        {
            VulkanTexture texture = i < preparedFrame.Textures.Length
                ? preparedFrame.Textures[i]
                : fallbackTexture;
            imageInfos[i] = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = texture.ImageView,
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
            DescriptorCount = MaxFrameTextures,
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

    protected DescriptorSet[] AllocateAndUpdateDescriptorSets(
        IReadOnlyList<PreparedDraw> preparedDraws,
        FrameOutput output,
        GpuBuffer drawParamBuffer,
        GpuBuffer triangleBuffer)
    {
        EnsureDescriptorPool(preparedDraws.Count);
        DescriptorSetLayout[] layouts = new DescriptorSetLayout[preparedDraws.Count];
        Array.Fill(layouts, descriptorSetLayout);
        DescriptorSet[] descriptorSets = new DescriptorSet[preparedDraws.Count];
        fixed (DescriptorSetLayout* layoutsPtr = layouts)
        fixed (DescriptorSet* descriptorSetsPtr = descriptorSets)
        {
            DescriptorSetAllocateInfo allocateInfo = new()
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = (uint)preparedDraws.Count,
                PSetLayouts = layoutsPtr
            };
            ThrowIfFailed(vk.AllocateDescriptorSets(device, &allocateInfo, descriptorSetsPtr), "vkAllocateDescriptorSets(actor)");
        }

        for (int i = 0; i < preparedDraws.Count; i++)
            UpdateDescriptorSet(descriptorSets[i], preparedDraws[i], output, drawParamBuffer, triangleBuffer);

        return descriptorSets;
    }

    protected void EnsureDescriptorPool(int setCount)
    {
        if (setCount <= 0)
            return;

        if (descriptorPool.Handle != 0 && setCount <= descriptorPoolCapacity)
        {
            ThrowIfFailed(vk.ResetDescriptorPool(device, descriptorPool, 0), "vkResetDescriptorPool(actor)");
            return;
        }

        if (descriptorPool.Handle != 0)
            vk.DestroyDescriptorPool(device, descriptorPool, null);

        descriptorPoolCapacity = Math.Max(setCount, 4);
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageImage,
            DescriptorCount = (uint)descriptorPoolCapacity
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint)(descriptorPoolCapacity * MaxFrameTextures)
        };
        poolSizes[2] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = (uint)(descriptorPoolCapacity * 2)
        };
        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)descriptorPoolCapacity,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes
        };
        ThrowIfFailed(vk.CreateDescriptorPool(device, &createInfo, null, out descriptorPool), "vkCreateDescriptorPool(actor)");
    }
}