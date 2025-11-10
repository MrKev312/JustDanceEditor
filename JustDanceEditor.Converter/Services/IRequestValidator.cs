namespace JustDanceEditor.Converter.Services;

public interface IRequestValidator
{
    void ValidateConversionRequest(ConversionRequest request);
    void ValidateTemplateFolder(string templatePath);
}