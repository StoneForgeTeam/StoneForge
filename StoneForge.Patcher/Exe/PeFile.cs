using System.IO;
using System.Text;

namespace StoneForge.Patcher;

/// <summary>Reading a Windows executable's headers.</summary>
internal static class PeFile
{
    /// <summary>Whether the PE file has a section with this name (Aurie's patch adds ".aurie").</summary>
    public static bool HasSection(string path, string name)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 0x3C;
        int peOffset = reader.ReadInt32();
        reader.BaseStream.Position = peOffset + 6;
        int sections = reader.ReadUInt16();
        reader.BaseStream.Position = peOffset + 20;
        int optionalSize = reader.ReadUInt16();
        long table = peOffset + 24 + optionalSize;
        for (int i = 0; i < sections; i++)
        {
            reader.BaseStream.Position = table + i * 40;
            string sectionName = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            if (sectionName == name)
                return true;
        }
        return false;
    }
}
