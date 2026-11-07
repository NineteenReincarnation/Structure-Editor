using System.Reflection;
using System.Runtime.Loader;

namespace StructureEditor;

// Bind only to the user's local game decoders. No game DLLs or assets are shipped with the editor.
internal static class GameTextureDecoders
{
    private delegate bool JoinDds(ReadOnlySpan<byte> bytes, out byte[] result, out int length);
    private delegate int DecodeBc(ReadOnlySpan<byte> bytes, int width, int height, out byte[] result);
    private static JoinDds join;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DecodeBc> decoders = new();
    private static Type Resolve(string assemblyName, string typeName)
    {
        string path = Path.Combine(Program.Paths.GameRoot ?? "", assemblyName + ".dll");
        if (!File.Exists(path)) throw new FileNotFoundException("当前游戏目录缺少贴图解码依赖：" + assemblyName + ".dll");
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path).GetType(typeName, true)!;
    }
    internal static bool TryJoin(ReadOnlySpan<byte> bytes, out byte[] result, out int length)
    {
        join ??= Resolve("Waylith", "Waylith.Graphics.Textures.DdsFile").GetMethod("TryJoin")!.CreateDelegate<JoinDds>();
        return join(bytes, out result, out length);
    }
    internal static void DecompressBc(string format, ReadOnlySpan<byte> bytes, int width, int height, out byte[] result)
    {
        var decode = decoders.GetOrAdd(format, name => Resolve("AssetRipper.TextureDecoder", "AssetRipper.TextureDecoder.Bc." + name)
            .GetMethods().Single(m => m.Name == "Decompress" && m.GetParameters().Last().ParameterType == typeof(byte[]).MakeByRefType()).CreateDelegate<DecodeBc>());
        decode(bytes, width, height, out result);
    }
    internal static Stream ZstdStream(Stream input) => (Stream)Activator.CreateInstance(
        Resolve("ZstdSharp", "ZstdSharp.DecompressionStream"), new object[] { input, 16384, true, true })!;
}
