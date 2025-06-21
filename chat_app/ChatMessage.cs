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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}