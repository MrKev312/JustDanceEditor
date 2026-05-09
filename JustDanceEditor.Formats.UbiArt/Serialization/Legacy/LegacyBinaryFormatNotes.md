# Legacy UbiArt Binary Serialization Notes

This format is the cooked Wii-era UAF-style binary representation used by the legacy `*_legacy.tpl`, tape, actor, and scene files.

Findings captured during the `LegacyEngineContentGenerator` rewrite:

- Multi-byte primitive values are big-endian.
- Strings are serialized as a big-endian 32-bit byte length followed by UTF-8 bytes.
- UbiArt paths are serialized as filename string, folder string, and the CRC32 of the filename as a big-endian 32-bit value.
- Tape files start with version `1`, a calculated tape version `(224 * clipCount) + 166`, type id `0x9E845460`, type size `0x8C` for JD2015 or `0x9C` for later legacy builds, then the clip count.
- Tape clip records are polymorphic by leading type id:
  - `0x955384A1`: motion clip
  - `0x52EC8962`: pictogram clip
  - `0xFD69B110`: gold effect clip
  - `0x68552A41`: karaoke clip
  - `0x2D8C885B`: sound set clip
  - `0x52E06A9A`: hide-user-interface clip
  - `0x101F9D2B`: gameplay event clip (common clip fields only)
- Resource-style template files share a common header: version `1`, serialized size, base type id `0x1B857BCE`, base type size `0x6C`, 28 reserved bytes, component count `1`, component type id, and component size.
- Scene files store a 23-byte header prefix followed by a single byte actor count. The typed serializer models this as version, scene id, 15 bytes of padding, and a byte count.
- Some actor/component tails are not cleanly 32-bit aligned in the source format. Those are modeled as named sequences with explicit byte padding rather than opaque byte arrays.
