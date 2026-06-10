using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace LlamaChatApp.Services;

public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("PDF file not found at {Path}", filePath);
            throw new FileNotFoundException("PDF file not found.", filePath);
        }

        return await Task.Run(() =>
        {
            _logger.LogInformation("Extracting text from PDF: {Path}", filePath);
            var sb = new StringBuilder();
            try
            {
                using var document = PdfDocument.Open(filePath);
                foreach (var page in document.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                _logger.LogInformation("Successfully extracted {Length} characters from PDF.", sb.Length);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF: {Path}", filePath);
                throw;
            }
        });
    }
}