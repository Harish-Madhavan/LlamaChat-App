using System;
using System.Text.Json.Serialization;

namespace LlamaChatApp;

public class AttachedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;

    [JsonIgnore]
    public string ExtractedText { get; set; } = string.Empty;

    public long FileSize { get; set; }
    public DateTime AttachedAt { get; set; } = DateTime.Now;
}
