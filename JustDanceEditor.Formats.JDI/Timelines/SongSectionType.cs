using System.ComponentModel;

namespace JustDanceEditor.Formats.JDI.Timelines;

[AttributeUsage(AttributeTargets.Field)]
public class ColorAttribute(byte r, byte g, byte b) : Attribute
{
    public byte R { get; } = r;
    public byte G { get; } = g;
    public byte B { get; } = b;
}

public enum SongSectionType
{
    [Color(230, 55, 55)]
    [Description("The introduction is a unique section that comes at the beginning of the piece.")]
    Intro,
    [Color(102, 190, 15)]
    [Description("The verse is the main part of a song. In popular music a verse roughly corresponds with a poetic stanza.")]
    Verse,
    [Color(230, 120, 55)]
    [Description("Also known as Pre-chorus. Contrasting section which also prepares for the return of the original material section")]
    Bridge,
    [Color(230, 55, 124)]
    [Description("The element of the song that repeats at least once both musically and lyrically")]
    Chorus,
    [Color(38, 194, 194)]
    [Description("Section of a piece played or sung by a single performer (example Sax or Guitar solo) designed to showcase an instrumentalist.")]
    Solo,
    [Color(230, 55, 55)]
    [Description("Occurs at the end of a song when the main lead vocal or a second lead vocal breaks away from the already established lyric and/or melody.")]
    Outro,
    [Color(230, 230, 0)]
    [Description("A thematic section in a song.")]
    Theme_A,
    [Color(200, 55, 200)]
    [Description("Typically, a song consists of first verse, pre-chorus, chorus, second verse, pre-chorus, chorus, middle eight, chorus.")]
    PreChorus,
    [Color(20, 125, 201)]
    [Description("A thematic section in a song.")]
    Theme_B,
    [Color(124, 124, 37)]
    [Description("A thematic section in a song.")]
    Theme_C,
    [Color(125, 125, 125)]
    [Description("A thematic section in a song.")]
    Theme_D,
    [Color(234, 166, 126)]
    [Description("Typically, a song consists of first verse, pre-chorus, chorus, second verse, pre-chorus, chorus, middle eight, chorus.")]
    Transition,
    [Color(24, 121, 121)]
    [Description("Typically, a song consists of first verse, pre-chorus, chorus, second verse, pre-chorus, chorus, middle eight, chorus.")]
    Development
}
