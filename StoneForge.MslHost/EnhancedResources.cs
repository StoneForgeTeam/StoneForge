using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ModShardLauncher;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace StoneForge.MslHost;

internal static class EnhancedResources
{
    private static IEnumerable<FileChunk> Chunks(ModFile file, string field) => (IEnumerable<FileChunk>)typeof(ModFile).GetField(field)!.GetValue(file)!;
    private static string Leaf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Invalid Enhanced resource name: " + name);
        return name;
    }
    private static void Stage(string output, string relative, byte[] bytes)
    {
        string path = Path.Combine(Path.GetDirectoryName(output)!, "resources", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    internal static void StageFonts(ModFile file, string output)
    {
        foreach (var chunk in Chunks(file, "FontFiles"))
            Stage(output, Path.Combine("fonts", Leaf(Path.GetFileName(chunk.name.Replace('\\', '/')))), file.GetFile(chunk.name));
    }

    internal static void PrepareAudioGroups(ModFile file, string output)
    {
        var loader = typeof(Main).Assembly.GetType("ModShardLauncher.AudioLoader", true)!;
        var determine = loader.GetMethod("DetermineAudioGroupFromPath", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var chunk in Chunks(file, "AudioFiles"))
        {
            string name = (string)determine.Invoke(null, new object[] { chunk.name })!;
            int index = DataLoader.data.AudioGroups.ToList().FindIndex(g => g.Name?.Content == name);
            if (index <= 0) continue;
            string path = Path.Combine(Path.GetDirectoryName(output)!, "audiogroup" + index + ".dat");
            // The fork's missing-group branch reads a MemoryStream; its library recognizes audio-only
            // FORM files by their .dat filename. Give it an actual staged .dat instead.
            if (!File.Exists(path)) File.WriteAllBytes(path, Convert.FromBase64String("Rk9STQwAAABBVURPBAAAAAAAAAA="));
        }
    }

    internal static void LoadShaders(ModFile file, string output)
    {
        var loader = typeof(Main).Assembly.GetType("ModShardLauncher.ShaderLoader", true)!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var chunks = Chunks(file, "ShaderFiles").ToArray();
        foreach (var group in chunks.GroupBy(c => c.name.Replace('\\', '/').Split('/')[^2]))
        {
            string name = Leaf(group.Key);
            var shader = DataLoader.data.Shaders.FirstOrDefault(s => s.Name?.Content == "shd_" + name);
            bool added = shader == null;
            shader ??= new UndertaleShader { Name = DataLoader.data.Strings.MakeString("shd_" + name), Type = UndertaleShader.ShaderType.GLSL_ES };
            var typeFile = group.FirstOrDefault(c => Path.GetFileName(c.name.Replace('\\', '/')) == "Type.txt");
            if (typeFile != null) shader.Type = (UndertaleShader.ShaderType)loader.GetMethod("ParseShaderType", flags)!
                .Invoke(null, new object[] { Encoding.UTF8.GetString(file.GetFile(typeFile.name)) })!;
            loader.GetMethod("LoadTextShaderFiles", flags)!.Invoke(null, new object[] { file, shader, name });
            loader.GetMethod("LoadBinaryShaderFiles", flags)!.Invoke(null, new object[] { file, shader, name });
            shader.VertexShaderAttributes.Clear();
            loader.GetMethod("LoadVertexShaderAttributes", flags)!.Invoke(null, new object[] { file, shader, name });
            foreach (var chunk in group)
                Stage(output, Path.Combine("shaders", name, Leaf(Path.GetFileName(chunk.name.Replace('\\', '/')))), file.GetFile(chunk.name));
            if (added) DataLoader.data.Shaders.Add(shader);
        }
    }

    internal static void ApplyPendingTextures()
    {
        var pending = (IDictionary)typeof(DataLoader).GetField("PendingTexturePatches")!.GetValue(null)!;
        var patches = pending.Values.Cast<object>().Select(v => ((int, byte[], ushort, ushort, ushort, ushort))v).ToArray();
        foreach (var group in patches.GroupBy(p => p.Item1))
        {
            var texture = DataLoader.data.EmbeddedTextures[group.Key];
            var format = texture.TextureData.Image.Format;
            using var png = new MemoryStream();
            texture.TextureData.Image.SavePng(png);
            png.Position = 0;
            using var image = Image.FromStream(png);
            using var bitmap = new Bitmap(image);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                foreach (var patch in group)
                {
                    using var source = new MemoryStream(patch.Item2);
                    using var replacement = Image.FromStream(source);
                    graphics.DrawImage(replacement, new Rectangle(patch.Item3, patch.Item4, patch.Item5, patch.Item6),
                        new Rectangle(0, 0, replacement.Width, replacement.Height), GraphicsUnit.Pixel);
                }
            }
            using var result = new MemoryStream();
            bitmap.Save(result, ImageFormat.Png);
            texture.TextureData.Image = GMImage.FromPng(result.ToArray()).ConvertToFormat(format);
        }
        pending.Clear();
        if (patches.Length > 0) Console.WriteLine("MSL Enhanced: Applied " + patches.Length + " queued texture replacement(s).");
    }
}
