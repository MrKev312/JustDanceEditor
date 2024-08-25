using System.Runtime.InteropServices;

namespace TextureConverter.TextureConverterHelpers;

public partial class PInvoke
{
    [LibraryImport("TexToolWrap.dll")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    public static partial IntPtr EncodeByCrunchUnity(out uint returnLength, IntPtr data, int mode, int level, uint width, uint height, uint ver, int mips);
}
