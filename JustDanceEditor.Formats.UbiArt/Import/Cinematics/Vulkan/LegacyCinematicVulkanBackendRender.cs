using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendRender(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : LegacyCinematicVulkanBackendTextures(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
{
    protected void RenderFrameLocked(
        CanvasBuffer outputCanvas,
        IReadOnlyList<LegacyCinematicGpuDrawItem> drawItems,
        byte[]? rawOutputBuffer,
        bool skipReadback,
        int frame)
    {
        EnsureFrameOutput(outputCanvas.Width, outputCanvas.Height);
        if (frameOutput == null)
            throw new InvalidOperationException("Vulkan frame output was not created.");
        int outputByteLength = checked(outputCanvas.Width * outputCanvas.Height * 4);
        if (rawOutputBuffer != null && rawOutputBuffer.Length < outputByteLength)
            throw new ArgumentException("Raw output buffer is smaller than the Vulkan frame.", nameof(rawOutputBuffer));

        PreparedFrame preparedFrame = PrepareFrame(drawItems, outputCanvas.Width, outputCanvas.Height);

        using GpuBuffer? drawParamBuffer = preparedFrame.DrawParams.Length == 0
            ? null
            : CreateAndUploadBuffer(
                MemoryMarshal.AsBytes(preparedFrame.DrawParams.AsSpan()),
                BufferUsageFlags.StorageBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        using GpuBuffer? triangleBuffer = preparedFrame.Triangles.Length == 0
            ? null
            : CreateAndUploadBuffer(
                MemoryMarshal.AsBytes(preparedFrame.Triangles.AsSpan()),
                BufferUsageFlags.StorageBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        DescriptorSet descriptorSet = drawParamBuffer == null
            ? default
            : AllocateAndUpdateFrameDescriptorSet(preparedFrame, frameOutput, drawParamBuffer, triangleBuffer ?? emptyTriangleBuffer!);

        try
        {
            CommandBuffer commandBuffer = BeginOneTimeCommands();
            TransitionOutputImage(
                commandBuffer,
                frameOutput,
                ImageLayout.TransferDstOptimal,
                AccessFlags.TransferWriteBit,
                PipelineStageFlags.TransferBit);
            ClearColorValue clearColor = new()
            {
                Uint32_0 = 0xFF000000
            };
            ImageSubresourceRange colorRange = CreateColorRange();
            vk.CmdClearColorImage(commandBuffer, frameOutput.Image, ImageLayout.TransferDstOptimal, &clearColor, 1, &colorRange);

            if (drawParamBuffer != null)
            {
                TransitionOutputImage(
                    commandBuffer,
                    frameOutput,
                    ImageLayout.General,
                    AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                    PipelineStageFlags.ComputeShaderBit);
                BufferBarrier(
                    commandBuffer,
                    drawParamBuffer.Buffer,
                    drawParamBuffer.Size,
                    AccessFlags.HostWriteBit,
                    AccessFlags.ShaderReadBit,
                    PipelineStageFlags.HostBit,
                    PipelineStageFlags.ComputeShaderBit);
                if (triangleBuffer != null)
                {
                    BufferBarrier(
                        commandBuffer,
                        triangleBuffer.Buffer,
                        triangleBuffer.Size,
                        AccessFlags.HostWriteBit,
                        AccessFlags.ShaderReadBit,
                        PipelineStageFlags.HostBit,
                        PipelineStageFlags.ComputeShaderBit);
                }

                vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, computePipeline);
                vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, &descriptorSet, 0, null);
                for (int i = 0; i < preparedFrame.DrawParams.Length; i++)
                {
                    GpuPushConstants pushConstants = new()
                    {
                        TintMaxY = preparedFrame.DrawParams[i].TintMaxY,
                        DrawIndex = i
                    };
                    vk.CmdPushConstants(commandBuffer, pipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(GpuPushConstants), &pushConstants);
                    uint groupsX = (uint)Math.Max(1, (preparedFrame.DrawParams[i].RectBlendMode.X + 15) / 16);
                    uint groupsY = (uint)Math.Max(1, (preparedFrame.DrawParams[i].RectBlendMode.Y + 15) / 16);
                    vk.CmdDispatch(commandBuffer, groupsX, groupsY, 1);
                    ImageBarrier(
                        commandBuffer,
                        frameOutput.Image,
                        ImageLayout.General,
                        ImageLayout.General,
                        AccessFlags.ShaderWriteBit,
                        AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                        PipelineStageFlags.ComputeShaderBit,
                        PipelineStageFlags.ComputeShaderBit,
                        colorRange);
                }
            }

            if (!skipReadback)
            {
                TransitionOutputImage(
                    commandBuffer,
                    frameOutput,
                    ImageLayout.TransferSrcOptimal,
                    AccessFlags.TransferReadBit,
                    PipelineStageFlags.TransferBit);
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
                    ImageExtent = new Extent3D((uint)outputCanvas.Width, (uint)outputCanvas.Height, 1)
                };
                vk.CmdCopyImageToBuffer(commandBuffer, frameOutput.Image, ImageLayout.TransferSrcOptimal, frameOutput.ReadbackBuffer.Buffer, 1, &copyRegion);
                BufferBarrier(
                    commandBuffer,
                    frameOutput.ReadbackBuffer.Buffer,
                    frameOutput.ReadbackBuffer.Size,
                    AccessFlags.TransferWriteBit,
                    AccessFlags.HostReadBit,
                    PipelineStageFlags.TransferBit,
                    PipelineStageFlags.HostBit);
            }

            EndOneTimeCommands(commandBuffer);

            if (!skipReadback)
            {
                if (rawOutputBuffer != null)
                    CopyReadbackBufferToBuffer(rawOutputBuffer, outputByteLength, frameOutput.ReadbackBuffer);
                else
                    CopyReadbackBufferToCanvas(outputCanvas, frameOutput.ReadbackBuffer);
            }
        }
        finally
        {
            preparedFrame.DisposeTransientTextures();
        }
    }

    protected List<PreparedDraw> PrepareDraws(
        IReadOnlyList<LegacyCinematicGpuDrawItem> drawItems,
        int outputWidth,
        int outputHeight,
        List<GpuTriangleParams> meshTriangles)
    {
        List<PreparedDraw> preparedDraws = new(drawItems.Count);
        foreach (LegacyCinematicGpuDrawItem drawItem in drawItems)
        {
            if (drawItem.Bounds.Width <= 0 || drawItem.Bounds.Height <= 0 || drawItem.Layers.Count == 0)
                continue;

            VulkanTexture[] textures = new VulkanTexture[MaxMaterialLayers];
            for (int i = 0; i < MaxMaterialLayers; i++)
                textures[i] = whiteTexture ?? throw new InvalidOperationException("White Vulkan texture was not created.");

            int meshTriangleOffset = meshTriangles.Count;
            int meshTriangleCount = AddMeshTriangles(drawItem, meshTriangles);
            GpuDrawParams parameters = CreateDrawParams(drawItem, outputWidth, outputHeight, meshTriangleOffset, meshTriangleCount);
            List<VulkanTexture>? transientTextures = null;
            for (int i = 0; i < drawItem.Layers.Count && i < MaxMaterialLayers; i++)
            {
                LegacyCinematicGpuLayer layer = drawItem.Layers[i];
                if (layer.Texture == null || layer.Sampler.TextureUsage == LegacyCinematicTextureUsage.NoTexture)
                    continue;

                VulkanTexture texture = GetOrCreateTexture(layer.Texture);
                textures[i] = texture;
            }

            preparedDraws.Add(new PreparedDraw(parameters, textures, transientTextures));
        }

        return preparedDraws;
    }

    protected PreparedFrame PrepareFrame(IReadOnlyList<LegacyCinematicGpuDrawItem> drawItems, int outputWidth, int outputHeight)
    {
        List<GpuDrawParams> drawParams = new(drawItems.Count);
        List<GpuTriangleParams> meshTriangles = [];
        List<VulkanTexture> frameTextures = [whiteTexture ?? throw new InvalidOperationException("White Vulkan texture was not created.")];
        Dictionary<VulkanTexture, int> frameTextureIndices = new(ReferenceEqualityComparer.Instance)
        {
            { frameTextures[0], 0 }
        };
        List<VulkanTexture>? transientTextures = null;

        foreach (LegacyCinematicGpuDrawItem drawItem in drawItems)
        {
            if (drawItem.Bounds.Width <= 0 || drawItem.Bounds.Height <= 0 || drawItem.Layers.Count == 0)
                continue;

            int[] layerTextureIndices = new int[MaxMaterialLayers];
            for (int i = 0; i < drawItem.Layers.Count && i < MaxMaterialLayers; i++)
            {
                LegacyCinematicGpuLayer layer = drawItem.Layers[i];
                if (layer.Texture == null || layer.Sampler.TextureUsage == LegacyCinematicTextureUsage.NoTexture)
                    continue;

                VulkanTexture texture = GetOrCreateTexture(layer.Texture);
                layerTextureIndices[i] = GetOrAddFrameTextureIndex(texture, frameTextures, frameTextureIndices);
            }

            int meshTriangleOffset = meshTriangles.Count;
            int meshTriangleCount = AddMeshTriangles(drawItem, meshTriangles);
            drawParams.Add(CreateDrawParams(
                drawItem,
                outputWidth,
                outputHeight,
                meshTriangleOffset,
                meshTriangleCount,
                layerTextureIndices));
        }

        return new PreparedFrame([.. drawParams], [.. meshTriangles], [.. frameTextures], transientTextures);
    }

    protected static int GetOrAddFrameTextureIndex(
        VulkanTexture texture,
        List<VulkanTexture> frameTextures,
        Dictionary<VulkanTexture, int> frameTextureIndices)
    {
        if (frameTextureIndices.TryGetValue(texture, out int existing))
            return existing;

        if (frameTextures.Count >= MaxFrameTextures)
            throw new InvalidOperationException($"Vulkan frame needs more than {MaxFrameTextures} texture descriptors.");

        int index = frameTextures.Count;
        frameTextures.Add(texture);
        frameTextureIndices.Add(texture, index);
        return index;
    }

    protected static int AddMeshTriangles(LegacyCinematicGpuDrawItem drawItem, List<GpuTriangleParams> meshTriangles)
    {
        if (drawItem.MeshTriangles == null || drawItem.MeshTriangles.Count == 0)
            return 0;

        foreach (LegacyCinematicGpuTriangle triangle in drawItem.MeshTriangles)
        {
            meshTriangles.Add(new GpuTriangleParams
            {
                Triangle0 = new Vector4(triangle.Point0.X, triangle.Point0.Y, triangle.Uv0.X, triangle.Uv0.Y),
                Triangle1 = new Vector4(triangle.Point1.X, triangle.Point1.Y, triangle.Uv1.X, triangle.Uv1.Y),
                Triangle2 = new Vector4(triangle.Point2.X, triangle.Point2.Y, triangle.Uv2.X, triangle.Uv2.Y)
            });
        }

        return drawItem.MeshTriangles.Count;
    }

    protected GpuDrawParams CreateDrawParams(
        LegacyCinematicGpuDrawItem drawItem,
        int outputWidth,
        int outputHeight,
        int meshTriangleOffset,
        int meshTriangleCount)
    {
        int[] layerTextureIndices = [0, 1, 2, 3];
        return CreateDrawParams(drawItem, outputWidth, outputHeight, meshTriangleOffset, meshTriangleCount, layerTextureIndices);
    }

    protected GpuDrawParams CreateDrawParams(
        LegacyCinematicGpuDrawItem drawItem,
        int outputWidth,
        int outputHeight,
        int meshTriangleOffset,
        int meshTriangleCount,
        IReadOnlyList<int> layerTextureIndices)
    {
        Vector2 origin = drawItem.Quad.TopLeft;
        Vector2 axisU = drawItem.Quad.TopRight - drawItem.Quad.TopLeft;
        Vector2 axisV = drawItem.Quad.BottomLeft - drawItem.Quad.TopLeft;
        float determinant = (axisU.X * axisV.Y) - (axisU.Y * axisV.X);
        LegacyCinematicUvRect uvOverride = drawItem.UvOverride ?? LegacyCinematicUvRect.Full;
        GpuDrawParams parameters = new()
        {
            OriginAxisU = new Vector4(origin.X, origin.Y, axisU.X, axisU.Y),
            AxisVDetAlpha = new Vector4(axisV.X, axisV.Y, determinant, drawItem.Alpha),
            TintMaxY = new Vector4((float)drawItem.Tint.Red, (float)drawItem.Tint.Green, (float)drawItem.Tint.Blue, drawItem.MaxY),
            Triangle0 = new Vector4(drawItem.TrianglePoint0.X, drawItem.TrianglePoint0.Y, drawItem.TriangleUv0.X, drawItem.TriangleUv0.Y),
            Triangle1 = new Vector4(drawItem.TrianglePoint1.X, drawItem.TrianglePoint1.Y, drawItem.TriangleUv1.X, drawItem.TriangleUv1.Y),
            Triangle2 = new Vector4(drawItem.TrianglePoint2.X, drawItem.TrianglePoint2.Y, drawItem.TriangleUv2.X, drawItem.TriangleUv2.Y),
            UvOverride = new Vector4(uvOverride.Left, uvOverride.Top, uvOverride.Right, uvOverride.Bottom),
            OutputRect = new GpuInt4(outputWidth, outputHeight, drawItem.Bounds.X, drawItem.Bounds.Y),
            RectBlendMode = new GpuInt4(drawItem.Bounds.Width, drawItem.Bounds.Height, drawItem.BlendMode, GetDrawMode(drawItem)),
            MeshOffsetCount = new GpuInt4(meshTriangleOffset, meshTriangleCount, 0, 0)
        };

        SetLayer(ref parameters.Layer0, drawItem.Layers, 0, layerTextureIndices);
        SetLayer(ref parameters.Layer1, drawItem.Layers, 1, layerTextureIndices);
        SetLayer(ref parameters.Layer2, drawItem.Layers, 2, layerTextureIndices);
        SetLayer(ref parameters.Layer3, drawItem.Layers, 3, layerTextureIndices);
        return parameters;
    }

    protected static int GetDrawMode(LegacyCinematicGpuDrawItem drawItem)
    {
        if (drawItem.MeshTriangles is { Count: > 0 })
            return DrawModeMesh;

        return drawItem.IsTriangle ? DrawModeTriangle : DrawModeQuad;
    }

    protected static void SetLayer(
        ref GpuLayerParams target,
        IReadOnlyList<LegacyCinematicGpuLayer> layers,
        int layerIndex,
        IReadOnlyList<int> layerTextureIndices)
    {
        if (layerIndex >= layers.Count)
        {
            target = GpuLayerParams.Disabled;
            return;
        }

        LegacyCinematicGpuLayer layer = layers[layerIndex];
        ComputeUvTransform(layer.Sampler, out Vector4 uvRow0, out Vector4 uvRow1);
        target = new GpuLayerParams
        {
            UvRow0 = uvRow0,
            UvRow1 = uvRow1,
            Diffuse = new Vector4(
                (float)layer.Sampler.DiffuseColor.Red,
                (float)layer.Sampler.DiffuseColor.Green,
                (float)layer.Sampler.DiffuseColor.Blue,
                (float)layer.Sampler.DiffuseColor.Alpha),
            Params0 = new GpuInt4(
                layer.Sampler.Enabled ? 1 : 0,
                layer.Sampler.BlendMode,
                (int)layer.Sampler.TextureUsage,
                (int)layer.Sampler.AddressModeU),
            Params1 = new GpuInt4((int)layer.Sampler.AddressModeV, layerTextureIndices[layerIndex], 0, 0)
        };
    }

    protected static void ComputeUvTransform(LegacyCinematicUvSampler sampler, out Vector4 row0, out Vector4 row1)
    {
        float m00 = 1.0f;
        float m01 = 0.0f;
        float m10 = 0.0f;
        float m11 = 1.0f;
        float tx = 0.0f;
        float ty = 0.0f;

        foreach (LegacyCinematicUvModifierState modifier in sampler.Modifiers)
        {
            if (Math.Abs(modifier.Rotation) > 0.000001f)
            {
                float cos = MathF.Cos(modifier.Rotation);
                float sin = MathF.Sin(modifier.Rotation);
                float r00 = cos;
                float r01 = -sin;
                float r10 = sin;
                float r11 = cos;
                float rtx = (modifier.RotationOffsetU * (1.0f - cos)) + (modifier.RotationOffsetV * sin);
                float rty = (modifier.RotationOffsetV * (1.0f - cos)) - (modifier.RotationOffsetU * sin);
                AppendAffine(ref m00, ref m01, ref m10, ref m11, ref tx, ref ty, r00, r01, r10, r11, rtx, rty);
            }

            float stx = (modifier.ScaleOffsetU * (1.0f - modifier.ScaleU)) + modifier.TranslationU;
            float sty = (modifier.ScaleOffsetV * (1.0f - modifier.ScaleV)) + modifier.TranslationV;
            AppendAffine(
                ref m00,
                ref m01,
                ref m10,
                ref m11,
                ref tx,
                ref ty,
                modifier.ScaleU,
                0.0f,
                0.0f,
                modifier.ScaleV,
                stx,
                sty);
        }

        row0 = new Vector4(m00, m01, tx, 0.0f);
        row1 = new Vector4(m10, m11, ty, 0.0f);
    }

    protected static void AppendAffine(
        ref float m00,
        ref float m01,
        ref float m10,
        ref float m11,
        ref float tx,
        ref float ty,
        float a00,
        float a01,
        float a10,
        float a11,
        float atx,
        float aty)
    {
        float next00 = (a00 * m00) + (a01 * m10);
        float next01 = (a00 * m01) + (a01 * m11);
        float next10 = (a10 * m00) + (a11 * m10);
        float next11 = (a10 * m01) + (a11 * m11);
        float nextTx = (a00 * tx) + (a01 * ty) + atx;
        float nextTy = (a10 * tx) + (a11 * ty) + aty;
        m00 = next00;
        m01 = next01;
        m10 = next10;
        m11 = next11;
        tx = nextTx;
        ty = nextTy;
    }
}