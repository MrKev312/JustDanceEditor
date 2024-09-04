using System.Runtime.InteropServices;

namespace TextureConverter.TextureConverterHelpers;

public partial class PInvoke
{
    [LibraryImport("DLLs/TexToolWrap.dll")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr EncodeByCrunchUnity(out uint returnLength, IntPtr data, int mode, int level, uint width, uint height, uint ver, int mips);

    public static IntPtr EncodeByCrunchUnitySafe(out uint returnLength, IntPtr data, int mode, int level, uint width, uint height, uint ver, int mips)
    {
        return EncodeByCrunchUnity(out returnLength, data, mode, level, width, height, ver, mips);
    }
}
