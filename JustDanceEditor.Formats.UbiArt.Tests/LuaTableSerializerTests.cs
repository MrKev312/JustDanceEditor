using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class LuaTableSerializerTests
{
    [Fact]
    public void DeserializeSongDesc_NormalizesUncookedEntryTables()
    {
        const string lua = """
            params = {
                Actor_Template = {
                    COMPONENTS = {
                        {
                            JD_SongDescTemplate = {
                                MapName = "GetGetDown",
                                JDVersion = 2021,
                                OriginalJDVersion = 2021,
                                PhoneImages = {
                                    { KEY = "cover", VAL = "world/maps/getgetdown/cover.jpg" },
                                    { KEY = "coach1", VAL = "world/maps/getgetdown/coach1.png" }
                                },
                                Tags = {
                                    { VAL = "Main" },
                                    { VAL = "Kids" }
                                },
                                DefaultColors = {
                                    { KEY = "lyrics", VAL = "0xFF1B34AA" }
                                }
                            }
                        }
                    }
                }
            }
            """;

        SongDesc songDesc = LuaTableSerializer.Deserialize<SongDesc>(lua);

        InfoComponent info = Assert.Single(songDesc.Components);
        Assert.Equal("GetGetDown", info.MapName);
        Assert.Equal("world/maps/getgetdown/cover.jpg", info.PhoneImages.Cover);
        Assert.Equal("world/maps/getgetdown/coach1.png", info.PhoneImages.Coach1);
        Assert.Equal(["Main", "Kids"], info.Tags);
        Assert.Equal([1f, 27 / 255f, 52 / 255f, 170 / 255f], info.DefaultColors.Lyrics);
    }

    [Fact]
    public void DeserializeSongDesc_AcceptsRegularObjectAndArrayShapes()
    {
        const string lua = """
            params = {
                Actor_Template = {
                    COMPONENTS = {
                        {
                            JD_SongDescTemplate = {
                                MapName = "RegularShapes",
                                PhoneImages = {
                                    Cover = "world/maps/regular/cover.jpg",
                                    Coach1 = "world/maps/regular/coach1.png"
                                },
                                Tags = { "Main" },
                                DefaultColors = {
                                    lyrics = { 1.0, 0.1, 0.2, 0.3 },
                                    theme = { 255, 1, 2, 3 }
                                }
                            }
                        }
                    }
                }
            }
            """;

        InfoComponent info = Assert.Single(LuaTableSerializer.Deserialize<SongDesc>(lua).Components);

        Assert.Equal("world/maps/regular/cover.jpg", info.PhoneImages.Cover);
        Assert.Equal("world/maps/regular/coach1.png", info.PhoneImages.Coach1);
        Assert.Equal(["Main"], info.Tags);
        Assert.Equal([1f, 0.1f, 0.2f, 0.3f], info.DefaultColors.Lyrics);
        Assert.Equal([255, 1, 2, 3], info.DefaultColors.Theme);
    }

    [Fact]
    public void DeserializeTapeEntryPaths_ReadsTapeCaseReferences()
    {
        const string lua = """
            params = {
                NAME = "Actor_Template",
                Actor_Template = {
                    COMPONENTS = {
                        {
                            NAME = "TapeCase_Template",
                            TapeCase_Template = {
                                TapesRack = {
                                    {
                                        TapeGroup = {
                                            Entries = {
                                                {
                                                    TapeEntry = {
                                                        Label = "TML_Motion",
                                                        Path = "World/MAPS/Song/Timeline/CustomDance.dtape"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            """;

        string path = Assert.Single(LuaTableSerializer.DeserializeTapeEntryPaths(lua));

        Assert.Equal("World/MAPS/Song/Timeline/CustomDance.dtape", path);
    }
}
