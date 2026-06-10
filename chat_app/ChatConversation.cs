using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LlamaChatApp;

public class ChatConversation : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string? _lastUsedModelPath;
    private string _id = Guid.NewGuid().ToString("N");

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LastUsedModelPath
    {
        get => _lastUsedModelPath;
        set { _lastUsedModelPath = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
    public ObservableCollection<AttachedDocument> AttachedDocuments { get; } = new ObservableCollection<AttachedDocument>();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
