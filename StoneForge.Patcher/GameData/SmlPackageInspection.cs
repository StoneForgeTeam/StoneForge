using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace StoneForge.Patcher;

// Read package bytes and PE metadata only. Discovery must never load a mod assembly.
internal static class SmlPackageInspection
{
    internal sealed record Inspection(bool EnhancedFormat, byte[] Assembly);
    private sealed record Chunk(int Offset, int Length);

    internal static Inspection Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x4D4C534D) throw new InvalidDataException("Not an MSL package: " + path);
        int versionLength = 0;
        while (versionLength < 24)
        {
            byte next = reader.ReadByte();
            if (next != 'v' && next != '.' && (next < '0' || next > '9')) { stream.Position--; break; }
            versionLength++;
        }
        if (versionLength == 0 || versionLength == 24) throw new InvalidDataException("Invalid MSL package version: " + path);
        var common = new List<Chunk>();
        for (int i = 0; i < 3; i++) common.AddRange(Section(reader));
        long start = stream.Position;
        byte[]? enhanced = TryAssembly(reader, common, start, enhanced: true);
        if (enhanced != null) return new(true, enhanced);
        byte[]? legacy = TryAssembly(reader, common, start, enhanced: false);
        return legacy != null ? new(false, legacy) : throw new InvalidDataException("Malformed MSL package: " + path);
    }

    private static List<Chunk> Section(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 100_000 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / 13)
            throw new InvalidDataException("Invalid MSL resource count.");
        var chunks = new List<Chunk>();
        for (int i = 0; i < count; i++)
        {
            int nameLength = reader.ReadInt32();
            if (nameLength <= 0 || nameLength > 4096 || nameLength > reader.BaseStream.Length - reader.BaseStream.Position - 8)
                throw new InvalidDataException("Invalid MSL resource name.");
            reader.BaseStream.Position += nameLength;
            int offset = reader.ReadInt32(), length = reader.ReadInt32();
            if (offset < 0 || length < 0) throw new InvalidDataException("Invalid MSL resource range.");
            chunks.Add(new(offset, length));
        }
        return chunks;
    }

    private static byte[]? TryAssembly(BinaryReader reader, List<Chunk> common, long start, bool enhanced)
    {
        try
        {
            var stream = reader.BaseStream;
            stream.Position = start;
            var chunks = new List<Chunk>(common);
            for (int i = 0; i < (enhanced ? 3 : 1); i++) chunks.AddRange(Section(reader));
            long dataLength = enhanced ? chunks.Sum(c => (long)c.Length) : chunks.Count == 0 ? 0 : (long)chunks[^1].Offset + chunks[^1].Length;
            if (chunks.Any(c => (long)c.Offset + c.Length > dataLength)) return null;
            if (dataLength > stream.Length - stream.Position - 4) return null;
            stream.Position += dataLength;
            int length = reader.ReadInt32();
            if (length < 2 || length > 64 * 1024 * 1024 || length > stream.Length - stream.Position) return null;
            var assembly = reader.ReadBytes(length);
            using var pe = new PEReader(new MemoryStream(assembly));
            if (!pe.HasMetadata || !pe.GetMetadataReader().IsAssembly) return null;
            return assembly;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or ArgumentOutOfRangeException or OverflowException)
        { return null; }
    }

    internal static bool RequiresEnhanced(Inspection package, string standardAssembly)
    {
        if (package.EnhancedFormat) return true;
        using var standardStream = File.OpenRead(standardAssembly);
        using var standard = new PEReader(standardStream);
        var baseline = standard.GetMetadataReader();
        var members = new Dictionary<string, HashSet<string>>();
        string DefinedName(TypeDefinitionHandle handle)
        {
            var type = baseline.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? baseline.GetString(type.Namespace) + "." + baseline.GetString(type.Name)
                : DefinedName(type.GetDeclaringType()) + "+" + baseline.GetString(type.Name);
        }
        foreach (var handle in baseline.TypeDefinitions)
        {
            var type = baseline.GetTypeDefinition(handle);
            string name = DefinedName(handle);
            members[name] = type.GetMethods().Select(h => baseline.GetString(baseline.GetMethodDefinition(h).Name))
                .Concat(type.GetFields().Select(h => baseline.GetString(baseline.GetFieldDefinition(h).Name))).ToHashSet();
        }
        using var pe = new PEReader(new MemoryStream(package.Assembly));
        var metadata = pe.GetMetadataReader();
        string? MslType(TypeReferenceHandle handle, int depth = 0)
        {
            if (depth > 64) throw new InvalidDataException("Invalid nested type references in MSL package.");
            var type = metadata.GetTypeReference(handle);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return MslType((TypeReferenceHandle)type.ResolutionScope, depth + 1) is { } parent ? parent + "+" + metadata.GetString(type.Name) : null;
            if (type.ResolutionScope.Kind != HandleKind.AssemblyReference) return null;
            var reference = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
            if (metadata.GetString(reference.Name) != "ModShardLauncher") return null;
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }
        foreach (var handle in metadata.TypeReferences)
            if (MslType(handle) is { } type && !members.ContainsKey(type)) return true;
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference && MslType((TypeReferenceHandle)member.Parent) is { } type &&
                members.TryGetValue(type, out var names) && !names.Contains(metadata.GetString(member.Name))) return true;
        }
        return false;
    }
}
