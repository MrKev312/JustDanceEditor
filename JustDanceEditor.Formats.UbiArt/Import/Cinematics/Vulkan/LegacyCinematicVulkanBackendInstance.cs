using Silk.NET.Vulkan;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal unsafe abstract class LegacyCinematicVulkanBackendInstance(
    Vk vk,
    Instance instance,
    PhysicalDevice physicalDevice,
    Device device,
    Queue graphicsQueue,
    uint graphicsQueueFamilyIndex,
    CommandPool commandPool,
    string deviceName) : LegacyCinematicVulkanBackendRender(vk, instance, physicalDevice, device, graphicsQueue, graphicsQueueFamilyIndex, commandPool, deviceName)
{
    protected static Instance CreateInstance(Vk vk)
    {
        fixed (byte* appName = "JustDanceEditor Legacy Cinematic\0"u8)
        fixed (byte* engineName = "JustDanceEditor\0"u8)
        {
            ApplicationInfo applicationInfo = new()
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                ApplicationVersion = Vk.MakeVersion(0, 1, 0),
                PEngineName = engineName,
                EngineVersion = Vk.MakeVersion(0, 1, 0),
                ApiVersion = Vk.Version10
            };

            InstanceCreateInfo createInfo = new()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &applicationInfo
            };

            ThrowIfFailed(vk.CreateInstance(&createInfo, null, out Instance instance), "vkCreateInstance");
            return instance;
        }
    }

    protected static bool TryPickPhysicalDevice(
        Vk vk,
        Instance instance,
        out PhysicalDevice physicalDevice,
        out uint queueFamilyIndex,
        out string deviceName,
        out string failureReason)
    {
        physicalDevice = default;
        queueFamilyIndex = 0;
        deviceName = string.Empty;
        failureReason = string.Empty;

        uint deviceCount = 0;
        ThrowIfFailed(vk.EnumeratePhysicalDevices(instance, &deviceCount, null), "vkEnumeratePhysicalDevices(count)");
        if (deviceCount == 0)
        {
            failureReason = "no Vulkan physical devices found";
            return false;
        }

        PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            ThrowIfFailed(vk.EnumeratePhysicalDevices(instance, &deviceCount, devicesPtr), "vkEnumeratePhysicalDevices");
        }

        foreach (PhysicalDevice candidate in devices)
        {
            if (!TryFindGraphicsQueueFamily(vk, candidate, out uint candidateQueueFamily))
                continue;

            PhysicalDeviceProperties properties;
            vk.GetPhysicalDeviceProperties(candidate, &properties);
            physicalDevice = candidate;
            queueFamilyIndex = candidateQueueFamily;
            deviceName = PtrToString(properties.DeviceName);
            return true;
        }

        failureReason = "no Vulkan graphics queue family found";
        return false;
    }

    protected static bool TryFindGraphicsQueueFamily(Vk vk, PhysicalDevice physicalDevice, out uint queueFamilyIndex)
    {
        queueFamilyIndex = 0;
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
        if (queueFamilyCount == 0)
            return false;

        QueueFamilyProperties[] queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamiliesPtr);
        }

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                queueFamilyIndex = i;
                return true;
            }
        }

        return false;
    }

    protected static Device CreateDevice(Vk vk, PhysicalDevice physicalDevice, uint queueFamilyIndex)
    {
        float queuePriority = 1.0f;
        PhysicalDeviceFeatures enabledFeatures = new()
        {
            ShaderSampledImageArrayDynamicIndexing = true
        };
        DeviceQueueCreateInfo queueCreateInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &enabledFeatures
        };

        ThrowIfFailed(vk.CreateDevice(physicalDevice, &createInfo, null, out Device device), "vkCreateDevice");
        return device;
    }

    protected static CommandPool CreateCommandPool(Vk vk, Device device, uint queueFamilyIndex)
    {
        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };
        ThrowIfFailed(vk.CreateCommandPool(device, &createInfo, null, out CommandPool commandPool), "vkCreateCommandPool");
        return commandPool;
    }

    protected DescriptorSetLayout CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[4];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageImage,
            StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorCount = MaxFrameTextures,
            DescriptorType = DescriptorType.CombinedImageSampler,
            StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[2] = new DescriptorSetLayoutBinding
        {
            Binding = 2,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[3] = new DescriptorSetLayoutBinding
        {
            Binding = 3,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            StageFlags = ShaderStageFlags.ComputeBit
        };

        DescriptorSetLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = bindings
        };
        ThrowIfFailed(vk.CreateDescriptorSetLayout(device, &createInfo, null, out DescriptorSetLayout layout), "vkCreateDescriptorSetLayout");
        return layout;
    }

    protected PipelineLayout CreatePipelineLayout(DescriptorSetLayout layout)
    {
        PushConstantRange pushConstantRange = new()
        {
            StageFlags = ShaderStageFlags.ComputeBit,
            Offset = 0,
            Size = (uint)sizeof(GpuPushConstants)
        };

        PipelineLayoutCreateInfo createInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &layout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        ThrowIfFailed(vk.CreatePipelineLayout(device, &createInfo, null, out PipelineLayout layoutResult), "vkCreatePipelineLayout");
        return layoutResult;
    }

    protected Pipeline CreateComputePipeline(PipelineLayout layout)
    {
        byte[] shaderCode = LegacyCinematicVulkanShaderCompiler.CompileComputeShader("legacy_cinematic_actor.comp");
        fixed (byte* shaderPtr = shaderCode)
        {
            ShaderModuleCreateInfo moduleCreateInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)shaderCode.Length,
                PCode = (uint*)shaderPtr
            };
            ThrowIfFailed(vk.CreateShaderModule(device, &moduleCreateInfo, null, out ShaderModule shaderModule), "vkCreateShaderModule(actor compute)");
            try
            {
                fixed (byte* entryPoint = "main\0"u8)
                {
                    ComputePipelineCreateInfo createInfo = new()
                    {
                        SType = StructureType.ComputePipelineCreateInfo,
                        Layout = layout,
                        Stage = new PipelineShaderStageCreateInfo
                        {
                            SType = StructureType.PipelineShaderStageCreateInfo,
                            Stage = ShaderStageFlags.ComputeBit,
                            Module = shaderModule,
                            PName = entryPoint
                        }
                    };
                    ThrowIfFailed(vk.CreateComputePipelines(device, default, 1, &createInfo, null, out Pipeline pipeline), "vkCreateComputePipelines(actor)");
                    return pipeline;
                }
            }
            finally
            {
                vk.DestroyShaderModule(device, shaderModule, null);
            }
        }
    }

    protected Sampler CreateNearestSampler()
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0.0f
        };
        ThrowIfFailed(vk.CreateSampler(device, &createInfo, null, out Sampler sampler), "vkCreateSampler");
        return sampler;
    }
}