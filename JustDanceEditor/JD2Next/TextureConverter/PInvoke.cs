using System.Runtime.InteropServices;

namespace TexturePlugin;

public partial class PInvoke
{
    [DllImport("TexToolWrap.dll")]
    public static extern uint DecodeByCrunchUnity(IntPtr data, IntPtr buf, int mode, uint width, uint height, uint byteSize);

    [DllImport("TexToolWrap.dll")]
    public static extern uint DecodeByPVRTexLib(IntPtr data, IntPtr buf, int mode, uint width, uint height);

    [DllImport("TexToolWrap.dll")]
    public static extern uint EncodeByCrunchUnity(IntPtr data, ref int checkoutId, int mode, int level, uint width, uint height, uint ver, int mips);

    [DllImport("TexToolWrap.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PickUpAndFree(IntPtr outBuf, uint size, int id);

    [DllImport("TexToolWrap.dll")]
    public static extern uint EncodeByPVRTexLib(IntPtr data, IntPtr buf, int mode, int level, uint width, uint height);

    [DllImport("TexToolWrap.dll")]
    public static extern uint EncodeByISPC(IntPtr data, IntPtr buf, int mode, int level, uint width, uint height);
}
