using System.Runtime.InteropServices;

namespace TexturePlugin;

public partial class PInvoke
{
	[LibraryImport("TexToolWrap.dll")]
	public static partial uint DecodeByCrunchUnity(IntPtr data, IntPtr buf, int mode, uint width, uint height, uint byteSize);

	[LibraryImport("TexToolWrap.dll")]
	public static partial uint DecodeByPVRTexLib(IntPtr data, IntPtr buf, int mode, uint width, uint height);

	[LibraryImport("TexToolWrap.dll")]
	public static partial uint EncodeByCrunchUnity(IntPtr data, ref int checkoutId, int mode, int level, uint width, uint height, uint ver, int mips);

	[LibraryImport("TexToolWrap.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static partial bool PickUpAndFree(IntPtr outBuf, uint size, int id);

	[LibraryImport("TexToolWrap.dll")]
	public static partial uint EncodeByPVRTexLib(IntPtr data, IntPtr buf, int mode, int level, uint width, uint height);

	[LibraryImport("TexToolWrap.dll")]
	public static partial uint EncodeByISPC(IntPtr data, IntPtr buf, int mode, int level, uint width, uint height);
}
