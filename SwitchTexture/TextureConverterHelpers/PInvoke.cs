using System.Runtime.InteropServices;

namespace SwitchTexture.TextureConverterHelpers;

public partial class PInvoke
{
    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint DecodeByCrunchUnity(nint data, nint buf, int mode, uint width, uint height, uint byteSize);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint DecodeByPVRTexLib(nint data, nint buf, int mode, uint width, uint height);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint EncodeByCrunchUnity(nint data, ref int checkoutId, int mode, int level, uint width, uint height, uint ver, int mips);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PickUpAndFree(nint outBuf, uint size, int id);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint EncodeByPVRTexLib(nint data, nint buf, int mode, int level, uint width, uint height);

    [DllImport("TexToolWrap.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint EncodeByISPC(nint data, nint buf, int mode, int level, uint width, uint height);
}
