# CrnLib

Managed .NET 10 support for the Unity-style Crunch CRN paths used by TextureConverter.

Implemented:

- DXT1 and DXT5 CRN header parsing/writing.
- Static Huffman bitstreams with frequency-based model encoding, palette decode/encode, and top-level/mip-level unpacking.
- ImageSharp `Image<TPixel>` decode/encode helpers for Unity-style Crunch textures.
- RGBA32 to Unity-style CRN encoding through managed BC1/BC3 block compression.
- High-quality Lanczos mip generation and nearest-palette fallback when Unity CRN palette limits are reached.

Not yet implemented:

- Original Binomial chunk-encoded CRN streams.
- ETC, DXN, DXT5A, DXT3, swizzled DXT5 variants, cubemaps, and segmented CRN files.
- The full Crunch rate-distortion/codebook optimizer and DDS/KTX utility surface.
