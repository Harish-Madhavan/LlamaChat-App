using System.Threading.Tasks;

namespace LlamaChatApp.Services;

public interface IPdfService
{
    Task<string> ExtractTextAsync(string filePath);
}