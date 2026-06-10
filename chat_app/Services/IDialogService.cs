namespace LlamaChatApp.Services;

public interface IDialogService
{
    string? ShowOpenFileDialog(string filter, string title);
    string? ShowSaveFileDialog(string filter, string title);
    void ShowErrorDialog(string message, string title = "Error");
    void ShowMessageDialog(string message, string title = "Information");
}