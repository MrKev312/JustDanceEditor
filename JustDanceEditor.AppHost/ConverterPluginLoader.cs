using JustDanceEditor.Conversion.Abstractions;

using System.Reflection;
using System.Runtime.Loader;

namespace JustDanceEditor.AppHost;

public static class ConverterPluginLoader
{
    private static readonly object ResolverLock = new();
    private static bool _resolverRegistered;
    private static string[] _assemblySearchDirectories = [];
    private static readonly List<string> LoadWarningsInternal = [];

    public static IReadOnlyList<string> AssemblySearchDirectories => _assemblySearchDirectories;
    public static IReadOnlyList<string> LoadWarnings => LoadWarningsInternal;

    public static IReadOnlyList<IConverterPlugin> LoadPlugins()
    {
        LoadWarningsInternal.Clear();
        string[] candidateDirectories = [.. EnumerateCandidateDirectories().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)];
        RegisterAssemblyResolver(candidateDirectories);

        foreach (string candidate in EnumerateCandidateAssemblies(candidateDirectories))
            TryLoadAssembly(candidate);

        return [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(type => typeof(IConverterPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            .Select(type => Activator.CreateInstance(type) as IConverterPlugin ?? throw new InvalidOperationException($"Could not create converter plugin '{type.FullName}'."))
            .GroupBy(plugin => plugin.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(plugin => plugin.Priority).First())
            .OrderBy(plugin => plugin.Priority)
            .ThenBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> EnumerateCandidateAssemblies(IEnumerable<string> candidateDirectories)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in candidateDirectories)
        {
            foreach (string dll in Directory.GetFiles(directory, "*.dll"))
            {
                if (seen.Add(dll))
                    yield return dll;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        string[] roots =
        [
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        ];

        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string convertersDirectory = Path.Combine(root, "Converters");

            yield return convertersDirectory;
            if (Directory.Exists(convertersDirectory))
            {
                foreach (string pluginDirectory in Directory.GetDirectories(convertersDirectory))
                    yield return pluginDirectory;
            }

            yield return root;
        }

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        foreach (string repoRoot in roots
            .Select(FindRepositoryRoot)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string projectDirectory in Directory.GetDirectories(repoRoot)
                .Where(IsDevelopmentDependencyProject))
            {
                yield return Path.Combine(projectDirectory, "bin", configuration, "net10.0");
            }
        }
    }

    private static bool IsDevelopmentDependencyProject(string projectDirectory)
    {
        string name = Path.GetFileName(projectDirectory);
        if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name is "JustDanceEditor.UI"
            or "JustDanceEditor.Cli"
            or "JustDanceEditor.GUI"
            or "JustDanceEditor.Editor"
            or "JustDanceEditor.AppHost")
        {
            return false;
        }

        return name.StartsWith("JustDanceEditor.", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterAssemblyResolver(string[] candidateDirectories)
    {
        lock (ResolverLock)
        {
            _assemblySearchDirectories = candidateDirectories;
            if (_resolverRegistered)
                return;

            AssemblyLoadContext.Default.Resolving += ResolveFromCandidateDirectories;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) => ResolveFromCandidateDirectories(AssemblyLoadContext.Default, new AssemblyName(args.Name));
            _resolverRegistered = true;
        }
    }

    private static Assembly? ResolveFromCandidateDirectories(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string fileName = $"{assemblyName.Name}.dll";
        foreach (string directory in _assemblySearchDirectories)
        {
            string candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate))
                continue;

            try
            {
                return context.LoadFromAssemblyPath(Path.GetFullPath(candidate));
            }
            catch (Exception ex)
            {
                LoadWarningsInternal.Add($"Failed to resolve '{assemblyName.Name}' from '{candidate}': {ex.Message}");
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "JustDanceEditor.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static void TryLoadAssembly(string path)
    {
        try
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            LoadWarningsInternal.Add($"Failed to load assembly '{path}': {ex.Message}");
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.OfType<Type>()];
        }
        catch
        {
            return [];
        }
    }
}
