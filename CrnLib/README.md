# CrnLib

Managed .NET 10 library for reading, writing, encoding, and decoding Unity-style Crunch CRN textures.

Implemented:

- CRN header parsing/writing for the CRN format IDs used by the original crnlib.
- Public format helpers for FourCC, fundamental format, bits per texel, bytes per block, and managed encode capability.
- Static Huffman bitstreams with frequency-based model encoding, palette decode/encode, and top-level/mip-level unpacking.
- ImageSharp `Image<TPixel>` decode/encode helpers for Unity-style Crunch textures.
- File validation, texture/file/level metadata, embedded level data extraction, segmented CRN base-file creation, and segmented level decode.
- RGBA32 to Unity-style CRN encoding through managed BC1/BC3/BC4/BC5 block compression.
- 2D textures and six-face cubemap encode/decode paths.
- CRN-writable formats: DXT1, DXT5, DXT5_CCxY, DXT5_xGxR, DXT5_xGBR, DXT5_AGBR, DXN_XY, DXN_YX, and DXT5A.
- High-quality Lanczos mip generation and nearest-palette fallback when Unity CRN palette limits are reached.

Not yet implemented:

- DXT3 and ETC1 CRN encoding; the original crnlib exposes these format IDs, but does not write them as CRN textures.
- The full original crnlib rate-distortion/codebook optimizer and DDS/KTX utility surface.
