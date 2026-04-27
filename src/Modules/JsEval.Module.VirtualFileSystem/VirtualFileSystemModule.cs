using Cocoar.JsEval;
using Zio;
using Zio.FileSystems;

#pragma warning disable CA1716 // 'Module' in namespace conflicts with keyword — cannot rename without a breaking change
namespace Cocoar.JsEval.Module.VirtualFileSystem;
#pragma warning restore CA1716

#pragma warning disable CA1822 // Instance methods required — Jint invokes these on a registered instance
public class VirtualFileSystemModule : IJsModule
{
    public SubFileSystem SubFileSystem(UPath uPath)
    {
        var fs = new PhysicalFileSystem();
        return new SubFileSystem(fs, uPath);
    }

    public AggregateFileSystem AggregateFileSystem()
    {
        return new AggregateFileSystem(true);
    }

    public AggregateFileSystem AggregateFileSystem(IFileSystem fileSystem)
    {
        return AggregateFileSystem([fileSystem]);
    }

    public AggregateFileSystem AggregateFileSystem(IFileSystem[] fileSystems)
    {
        var aggregateFileSystem = AggregateFileSystem();
        foreach (var fileSystem in fileSystems)
        {
            aggregateFileSystem.AddFileSystem(fileSystem);
        }

        return aggregateFileSystem;
    }

    public MountFileSystem MountFileSystem()
    {
        return new MountFileSystem(true);
    }

    public UPath BuildUPath(string path)
    {
        return new UPath(path);
    }

    public UPath BuildUPath(string[] paths)
    {
        return UPath.Combine(paths.Select(p => (UPath)p).ToArray());
    }
}
#pragma warning restore CA1822
