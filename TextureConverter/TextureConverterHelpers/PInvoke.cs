using System.Runtime.InteropServices;

namespace TextureConverter.TextureConverterHelpers;

public partial class PInvoke
{
    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint EncodeByCrunchUnity(nint data, ref int checkoutId, int mode, int level, uint width, uint height, uint ver, int mips);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PickUpAndFree(nint outBuf, uint size, int id);
}
