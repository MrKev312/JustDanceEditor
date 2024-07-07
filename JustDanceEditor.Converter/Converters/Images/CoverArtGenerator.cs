using HtmlAgilityPack;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Converter.Converters.Images;

public static class CoverArtGenerator
{
    public static bool ExistingCover(out Image<Rgba32>? coverImage, ConvertUbiArtToUnity convert)
    {
        coverImage = null;

        string[] paths =
        [
            Path.Combine(convert.InputFolder, "cover.png"),
            Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_cover_online.tga.png"),
            Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_cover_generic.tga.png")
        ];

        foreach (string path in paths)
        {
            if (TryLoadImage(ref coverImage, path))
                return true;
        }

        return false;
    }

    private static bool TryLoadImage(ref Image<Rgba32>? coverImage, string path)
    {
        if (!File.Exists(path))
            return false;

        // Load the image
        coverImage = LoadImage(path);

        // If the image is a square, dispose of it and return false
        if (coverImage.Width == coverImage.Height)
        {
            coverImage.Dispose();
            return false;
        }

        return true;
    }

    private static Image<Rgba32> LoadImage(string path)
    {
        // Load the image
        Image<Rgba32> image = Image.Load<Rgba32>(path);
        // Resize the image to 640x360
        image.Mutate(x => x.Resize(640, 360));
        return image;
    }

    public static bool TryCoverWeb(out Image<Rgba32>? coverImage, ConvertUbiArtToUnity convert)
    {
        coverImage = null;
        // Download one from https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B
        // Load the webpage
        HttpClient client = new();
        string html = client.GetStringAsync("https://justdance.fandom.com/wiki/User_blog:Sweet_King_Candy/Extended_Covers_for_Just_Dance_%2B").Result;

        // Convert the html to a document
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        // Get the class called "fandom-table"[0]/tbody
        HtmlNode table = doc.DocumentNode.SelectNodes("//table[@class='fandom-table']")[0];
        HtmlNode htmlNode = table.SelectSingleNode("tbody");

        // Now get the node where the first td is the map name
        List<HtmlNode> rows = htmlNode.SelectNodes("tr").Skip(1).Where(x => x.SelectSingleNode("td").InnerText == convert.SongData.SongDesc.COMPONENTS[0].Title).ToList();
        // Select the first one, if it exists, if not, null
        HtmlNode? row = rows.Count > 0 ? rows[0] : null;

        // If the cover doesn't exist, return false
        if (row is null)
            return false;

        // If both the last or second to last td's are empty or "N/A", then the cover doesn't exist
        HtmlNodeCollection tds = row.SelectNodes("td");
        string coverUrl = "";

        // Get the cover url
        if (tds[^1].InnerText.Contains("PlaceHolderCover2023") || tds[^1].InnerText == "")
            coverUrl = tds[^1].SelectSingleNode("a").Attributes["href"].Value;
        else if (tds[^2].InnerText.Contains("PlaceHolderCover2023") || tds[^2].InnerText == "")
            coverUrl = tds[^2].SelectSingleNode("a").Attributes["href"].Value;
        else
            // Couldn't find the cover, return false
            return false;

        // Load the image
        Stream coverStream = client.GetStreamAsync(coverUrl).Result;
        coverImage = Image.Load<Rgba32>(coverStream);
        coverImage.Mutate(x => x.Resize(640, 360));

        // Success!
        return true;
    }

    public static void GenerateOwnCover(out Image<Rgba32> coverImage, ConvertUbiArtToUnity convert)
    {
        // Manually create the cover
        // Now we gotta make a custom texture from the scraps we have in the menu art folder
        // First we load in the background
        string backgroundPath = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_map_bkg.tga.png");
        coverImage = Image.Load<Rgba32>(backgroundPath);

        // Stretch it to 2048x1024, this shouldn't do anything to correctly sized images
        coverImage.Mutate(x => x.Resize(2048, 1024));

        // Then we load in the albumcoach
        string albumCoachPath = Path.Combine(convert.TempMenuArtFolder, $"{convert.SongData.Name}_cover_albumcoach.tga.png");
        Image<Rgba32> albumCoach = Image.Load<Rgba32>(albumCoachPath);

        // Then we place the albumcoach on top of the background in the center
        // The background is 2048x1024 and the albumcoach is 1024x1024
        // So we place it at 512, 0
        coverImage.Mutate(x => x.DrawImage(albumCoach, new Point(512, 0), 1));

        // Now we resize the image down to 720x360
        coverImage.Mutate(x => x.Resize(720, 360));

        // Now we crop the image to 640x360 centered
        coverImage.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));
    }
}
