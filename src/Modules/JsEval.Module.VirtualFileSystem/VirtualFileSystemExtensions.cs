using Zio;
using Zio.FileSystems;

namespace Cocoar.JsEval.Module.VirtualFileSystem;

public static class VirtualFileSystemExtensions
{
    public static bool IsDirectory(this FileSystemEntry fileSystemInfo)
    {
        return fileSystemInfo.Attributes.HasFlag(FileAttributes.Directory);
    }

    public static bool IsFile(this FileSystemEntry fileSystemInfo)
    {
        return !fileSystemInfo.Attributes.HasFlag(FileAttributes.Directory);
    }

    public static FileEntry AsFileEntry(this FileSystemEntry fileSystemInfo)
    {
        return TryAsFileEntry(fileSystemInfo, out var fileInfo) ? fileInfo! : throw new FileLoadException("FileSystemEntry is not a File!");
    }

    public static bool TryAsFileEntry(this FileSystemEntry fileSystemEntry, out FileEntry? fileInfo)
    {
        if (IsFile(fileSystemEntry))
        {
            fileInfo = new FileEntry(fileSystemEntry.FileSystem, fileSystemEntry.Path);
            return true;
        }

        fileInfo = null;
        return false;
    }

    public static Stream ReadAsStream(this FileEntry fileEntry)
    {
        return fileEntry.Open(FileMode.Open, FileAccess.Read);
    }

    public static MountFileSystem Mount(this MountFileSystem mountFileSystem, string path, IFileSystem fileSystem)
    {
        mountFileSystem.Mount(new UPath(path), fileSystem);
        return mountFileSystem;
    }
}
