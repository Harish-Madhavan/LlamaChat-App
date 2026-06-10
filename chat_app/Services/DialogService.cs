using Microsoft.Win32;
using System.Windows;

namespace LlamaChatApp.Services;

public class DialogService : IDialogService
{
    public string? ShowOpenFileDialog(string filter, string title)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };
        return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string title)
    {
        var saveFileDialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title
        };
        return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
    }

    public void ShowErrorDialog(string message, string title = "Error")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowMessageDialog(string message, string title = "Information")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}