using Silk.NET.Shaderc;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

internal static class LegacyCinematicVulkanShaderCompiler
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> ComputeShaderCache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[] CompileComputeShader(string shaderFileName)
    {
        Lazy<byte[]> bytecode = ComputeShaderCache.GetOrAdd(
            shaderFileName,
            static name => new Lazy<byte[]>(
                () => CompileComputeShaderCore(name),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return bytecode.Value;
    }

    private static byte[] CompileComputeShaderCore(string shaderFileName)
    {
        string shaderPath = FindShaderPath(shaderFileName);
        byte[] source = File.ReadAllBytes(shaderPath);

        try
        {
            return CompileWithBundledShaderc(shaderFileName, shaderPath, source);
        }
        catch (Exception exception) when (IsShadercRuntimeFailure(exception))
        {
            string? precompiledPath = TryFindPrecompiledShaderPath(shaderFileName);
            if (precompiledPath != null)
                return File.ReadAllBytes(precompiledPath);

            throw new InvalidOperationException(
                $"Could not compile Vulkan shader {shaderFileName}. " +
                "The GLSL source is present, but the bundled Shaderc native runtime was not available. " +
                "Restore the Silk.NET.Shaderc.Native NuGet package for this runtime or include generated SPIR-V as a fallback.",
                exception);
        }
    }

    private static unsafe byte[] CompileWithBundledShaderc(string shaderFileName, string shaderPath, byte[] source)
    {
        Shaderc shaderc = Shaderc.GetApi();
        Compiler* compiler = null;
        CompileOptions* options = null;
        CompilationResult* result = null;

        try
        {
            compiler = shaderc.CompilerInitialize();
            if (compiler == null)
                throw new InvalidOperationException("Shaderc failed to initialize a compiler.");

            options = shaderc.CompileOptionsInitialize();
            if (options == null)
                throw new InvalidOperationException("Shaderc failed to initialize compile options.");

            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, 0);

            fixed (byte* sourceBytes = source)
            {
                result = shaderc.CompileIntoSpv(
                    compiler,
                    sourceBytes,
                    (nuint)source.Length,
                    ShaderKind.ComputeShader,
                    shaderPath,
                    "main",
                    options);
            }

            if (result == null)
                throw new InvalidOperationException($"Shaderc did not return a result for {shaderFileName}.");

            CompilationStatus status = shaderc.ResultGetCompilationStatus(result);
            if (status != CompilationStatus.Success)
            {
                string message = GetResultMessage(shaderc, result);
                throw new InvalidOperationException($"Shaderc failed for {shaderFileName}: {message}");
            }

            nuint length = shaderc.ResultGetLength(result);
            if (length > int.MaxValue)
                throw new InvalidOperationException($"Shaderc produced too much bytecode for {shaderFileName}: {length} bytes.");

            byte* resultBytes = shaderc.ResultGetBytes(result);
            if (resultBytes == null)
                throw new InvalidOperationException($"Shaderc produced no bytecode for {shaderFileName}.");

            byte[] bytecode = new byte[(int)length];
            Marshal.Copy((nint)resultBytes, bytecode, 0, bytecode.Length);
            return bytecode;
        }
        finally
        {
            if (result != null)
                shaderc.ResultRelease(result);
            if (options != null)
                shaderc.CompileOptionsRelease(options);
            if (compiler != null)
                shaderc.CompilerRelease(compiler);
        }
    }

    private static unsafe string GetResultMessage(Shaderc shaderc, CompilationResult* result)
    {
        byte* messageBytes = shaderc.ResultGetErrorMessage(result);
        if (messageBytes == null)
            return string.Empty;

        return Marshal.PtrToStringUTF8((nint)messageBytes) ?? string.Empty;
    }

    private static bool IsShadercRuntimeFailure(Exception exception)
    {
        return exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or TypeInitializationException;
    }

    private static string? TryFindPrecompiledShaderPath(string shaderFileName)
    {
        string bytecodeFileName = Path.ChangeExtension(shaderFileName, ".spv");
        string relativePath = Path.Combine("Import", "Cinematics", "Vulkan", "Shaders", bytecodeFileName);
        string outputPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(outputPath))
            return outputPath;

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string sourcePath = Path.Combine(
                directory.FullName,
                "JustDanceEditor.Formats.UbiArt",
                relativePath);
            if (File.Exists(sourcePath))
                return sourcePath;

            directory = directory.Parent;
        }

        string currentPath = Path.Combine(Environment.CurrentDirectory, "JustDanceEditor.Formats.UbiArt", relativePath);
        return File.Exists(currentPath)
            ? currentPath
            : null;
    }

    private static string FindShaderPath(string shaderFileName)
    {
        string relativePath = Path.Combine("Import", "Cinematics", "Vulkan", "Shaders", shaderFileName);
        string outputPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(outputPath))
            return outputPath;

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string sourcePath = Path.Combine(
                directory.FullName,
                "JustDanceEditor.Formats.UbiArt",
                relativePath);
            if (File.Exists(sourcePath))
                return sourcePath;

            directory = directory.Parent;
        }

        string currentPath = Path.Combine(Environment.CurrentDirectory, "JustDanceEditor.Formats.UbiArt", relativePath);
        if (File.Exists(currentPath))
            return currentPath;

        throw new FileNotFoundException($"Could not locate Vulkan shader source {shaderFileName}.");
    }
}