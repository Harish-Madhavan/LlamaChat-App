// ChatMessage.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace LlamaChatApp
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _isEditing;
        private string? _originalText;

        public string Author { get; set; } = string.Empty;
        public Brush AuthorBrush { get; set; } = Brushes.Black;
        public Brush BubbleBackground { get; set; } = Brushes.LightGray;
        public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;

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

        // Store original text when editing begins so we can cancel
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}