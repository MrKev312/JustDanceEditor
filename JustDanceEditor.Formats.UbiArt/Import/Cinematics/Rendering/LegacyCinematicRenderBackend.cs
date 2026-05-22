using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal enum LegacyCinematicRenderBackendKind
{
    Cpu,
    Vulkan,
    OpenGl
}

internal sealed class LegacyCinematicRenderBackend : IDisposable
{
    private readonly IDisposable? backend;
    public static LegacyCinematicRenderBackendKind DefaultKind => LegacyCinematicRenderBackendKind.Vulkan;

    private LegacyCinematicRenderBackend(
        LegacyCinematicRenderBackendKind kind,
        string name,
        IDisposable? backend)
    {
        Kind = kind;
        Name = name;
        this.backend = backend;
    }

    public LegacyCinematicRenderBackendKind Kind { get; }
    public string Name { get; }
    public LegacyCinematicVulkanBackend? Vulkan => backend as LegacyCinematicVulkanBackend;

    public static LegacyCinematicRenderBackend Create(int width, int height, out string fallbackReason)
    {
        fallbackReason = string.Empty;
        if (DefaultKind == LegacyCinematicRenderBackendKind.Vulkan)
        {
            if (LegacyCinematicVulkanBackend.TryCreate(width, height, out LegacyCinematicVulkanBackend? vulkan, out string failureReason) &&
                vulkan != null)
            {
                return new LegacyCinematicRenderBackend(LegacyCinematicRenderBackendKind.Vulkan, "vulkan", vulkan);
            }

            fallbackReason = failureReason;
        }

        return new LegacyCinematicRenderBackend(LegacyCinematicRenderBackendKind.Cpu, "cpu", null);
    }

    public static LegacyCinematicRenderBackend Create(int width, int height) =>
        Create(width, height, out _);

    public void Dispose() => backend?.Dispose();
}