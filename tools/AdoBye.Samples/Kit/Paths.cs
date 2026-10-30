namespace AdoBye.Samples.Kit;

/// <summary>Repository paths, found by walking up from the working directory or the binary.</summary>
public static class Paths
{
    static string? _root;

    public static string Root => _root ??= Find();

    public static string Samples => Path.Combine(Root, "samples");

    public static string Fonts => Path.Combine(Samples, "_gen", "fonts");

    static string Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "samples")) && Directory.Exists(Path.Combine(d.FullName, "tools", "AdoBye.Samples")))
                    return d.FullName;
        throw new DirectoryNotFoundException("repository root not found");
    }
}
