using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using Silk.NET.Vulkan;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal sealed unsafe class LegacyCinematicVulkanBackend : LegacyCinematicVulkanBackendInstance
{
    private LegacyCinematicVulkanBackend(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue graphicsQueue,
        uint graphicsQueueFamilyIndex,
        CommandPool commandPool,
        string deviceName)
        : base(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
    {
        descriptorSetLayout = CreateDescriptorSetLayout();
        pipelineLayout = CreatePipelineLayout(descriptorSetLayout);
        computePipeline = CreateComputePipeline(pipelineLayout);
        nearestSampler = CreateNearestSampler();
        whiteTexture = CreateTexture(
            LegacyCinematicGpuTextureSource.FromPixels(
                "legacy-cinematic-white-texture",
                "white",
                1,
                1,
                [CreateOpaqueWhitePixel()],
                cacheable: true));
        emptyTriangleBuffer = CreateBufferResource(
            (ulong)sizeof(GpuTriangleParams),
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    public static bool TryCreate(
        int width,
        int height,
        out LegacyCinematicVulkanBackend? backend,
        out string failureReason)
    {
        backend = null;
        failureReason = string.Empty;

        try
        {
            Vk vk = Vk.GetApi();
            Instance instance = CreateInstance(vk);
            if (!TryPickPhysicalDevice(vk, instance, out PhysicalDevice physicalDevice, out uint queueFamilyIndex, out string deviceName, out failureReason))
            {
                vk.DestroyInstance(instance, null);
                return false;
            }

            Device device = CreateDevice(vk, physicalDevice, queueFamilyIndex);
            vk.GetDeviceQueue(device, queueFamilyIndex, 0, out Queue graphicsQueue);
            CommandPool commandPool = CreateCommandPool(vk, device, queueFamilyIndex);
            LegacyCinematicVulkanBackend created = new(
                vk,
                instance,
                physicalDevice,
                device,
                graphicsQueue,
                queueFamilyIndex,
                commandPool,
                deviceName);

            backend = created;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = ex.Message;
            try
            {
                backend?.Dispose();
            }
            catch
            {
                // Best-effort cleanup after a failed probe.
            }

            return false;
        }
    }

    public bool TryRenderFrame(
        CanvasBuffer outputCanvas,
        IReadOnlyList<LegacyCinematicGpuDrawItem> drawItems,
        out string failureReason,
        byte[]? rawOutputBuffer = null,
        bool skipReadback = false,
        int frame = -1)
    {
        failureReason = string.Empty;
        try
        {
            lock (renderLock)
            {
                RenderFrameLocked(outputCanvas, drawItems, rawOutputBuffer, skipReadback, frame);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    public override void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        vk.DeviceWaitIdle(device);
        frameOutput?.Dispose();
        emptyTriangleBuffer?.Dispose();
        foreach (VulkanTexture texture in textureCache.Values)
            texture.Dispose();
        foreach (VulkanTexture texture in transientTextureCache.Values)
            texture.Dispose();

        whiteTexture?.Dispose();
        if (descriptorPool.Handle != 0)
            vk.DestroyDescriptorPool(device, descriptorPool, null);
        vk.DestroySampler(device, nearestSampler, null);
        vk.DestroyPipeline(device, computePipeline, null);
        vk.DestroyPipelineLayout(device, pipelineLayout, null);
        vk.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);
        vk.DestroyCommandPool(device, commandPool, null);
        vk.DestroyDevice(device, null);
        vk.DestroyInstance(instance, null);
        vk.Dispose();
    }
}