using LlamaChatApp.Services;
using LlamaChatApp.ViewModels;
using System;
using System.Windows;
using System.Windows.Media;

namespace LlamaChatApp;

public class ChatMessage : ViewModelBase
{
    private string _text = string.Empty;
    private bool _isEditing;
    private string? _originalText;
    private GenerationMetrics _metrics;
    private bool _hasMetrics;

    public string Author { get; set; } = string.Empty;
    public Brush AuthorBrush { get; set; } = Brushes.Black;
    public Brush BubbleBackground { get; set; } = Brushes.LightGray;
    public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged();
            }
        }
    }

    public GenerationMetrics Metrics
    {
        get => _metrics;
        set
        {
            _metrics = value;
            OnPropertyChanged();
            HasMetrics = true;
        }
    }

    public bool HasMetrics
    {
        get => _hasMetrics;
        set
        {
            _hasMetrics = value;
            OnPropertyChanged();
        }
    }

    public string? OriginalText
    {
        get => _originalText;
        private set
        {
            _originalText = value;
            OnPropertyChanged();
        }
    }

    public void BeginEdit()
    {
        if (!IsEditing)
        {
            OriginalText = Text;
            IsEditing = true;
        }
    }

    public void SaveEdit()
    {
        if (IsEditing)
        {
            OriginalText = null;
            IsEditing = false;
        }
    }

    public void CancelEdit()
    {
        if (IsEditing)
        {
            Text = OriginalText ?? Text;
            OriginalText = null;
            IsEditing = false;
        }
    }
}
