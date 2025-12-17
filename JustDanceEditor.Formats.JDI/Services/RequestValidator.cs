namespace JustDanceEditor.Formats.JDI.Services;

// TODO: Remove this from here, as this is all format specific validation logic.
// Each format should implement a RequestValidator of its own.
public class RequestValidator : IRequestValidator
{
    public void ValidateConversionRequest(ConversionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath) || !Directory.Exists(request.InputPath))
            throw new FileNotFoundException("Input folder not found", request.InputPath);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is not a valid URI", nameof(request.OutputPath));

        // This is a side-effect; consider if validators should modify the file system.
        // For now, keeping original behavior.
        Directory.CreateDirectory(request.OutputPath);
    }

    public void ValidateTemplateFolder(string templatePath)
    {
        string[] foldersToValidate = [
            Path.Combine(templatePath, "Cover"),
            Path.Combine(templatePath, "MapPackage"),
            Path.Combine(templatePath, "CoachesLarge"),
            Path.Combine(templatePath, "CoachesSmall"),
            Path.Combine(templatePath, "SongTitleLogo")
        ];

        foreach (string folder in foldersToValidate)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException($"The template subfolder {folder} is missing. Please ensure the template structure is correct.");
            }
        }

        // Check for files in each required subfolder
        string[] requiredTemplateSubFolders = ["Cover", "MapPackage", "CoachesLarge", "CoachesSmall", "SongTitleLogo"];

        foreach (string subFolderName in requiredTemplateSubFolders)
        {
            string fullSubFolderPath = Path.Combine(templatePath, subFolderName);
            if (!Directory.Exists(fullSubFolderPath))
            {
                throw new DirectoryNotFoundException($"The template folder {fullSubFolderPath} is missing.");
            }

            if (Directory.GetFiles(fullSubFolderPath).Length == 0)
            {
                throw new FileNotFoundException($"The template folder {fullSubFolderPath} is empty. Please put a template file in the folder.");
            }
        }
    }
}