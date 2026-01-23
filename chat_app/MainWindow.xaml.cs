// MainWindow.xaml.cs
using LlamaChatApp.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LlamaChatApp
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = (MainViewModel)DataContext;
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;

            _viewModel.RequestFileDialog = () =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "GGUF Model Files (*.gguf)|*.gguf|All files (*.*)|*.*",
                    Title = "Select a Llama Model File"
                };
                                return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;      
                            };
                
                            _viewModel.RequestSaveFileDialog = (filter, title) =>
                            {
                                var saveFileDialog = new SaveFileDialog
                                {
                                    Filter = filter,
                                    Title = title
                                };
                                return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : null;
                            };
                
                            _viewModel.RequestOpenFileDialog = (filter, title) =>
                            {
                                var openFileDialog = new OpenFileDialog
                                {
                                    Filter = filter,
                                    Title = title
                                };
                                return openFileDialog.ShowDialog() == true ? openFileDialog.FileName : null;
                            };
                
                            _viewModel.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
            // Also respond to current conversation changes so we can re-subscribe
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ChatMessages))
            {
                // Re-subscribe to the new collection
                _viewModel.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.LoadSettingsCommand.Execute(null);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _viewModel.SaveSettingsCommand.Execute(null);
            try
            {
                (_viewModel as IDisposable)?.Dispose();
            }
            catch { }
        }

        // This is the improved auto-scroll logic
        private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Delay allows the ListView to render the new item before we try to scroll
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (VisualTreeHelper.GetChild(ChatListView, 0) is Decorator border)
                    {
                        var scrollViewer = border.Child as ScrollViewer;
                        // Auto-scroll only if the user is already near the bottom
                        if (scrollViewer != null && scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 30)
                        {
                            ChatListView.ScrollIntoView(_viewModel.ChatMessages.Last());
                        }
                    }
                }));
            }
        }

        // Handle Enter key for sending message (Shift+Enter for newline)
        private async void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    // Allow newline for Ctrl+Enter or Shift+Enter
                    return;
                }

                // Plain Enter sends the message
                e.Handled = true;
                if (_viewModel.SendMessageCommand.CanExecute(null))
                {
                    _viewModel.SendMessageCommand.Execute(null);
                }
            }
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ChatMessage msg)
            {
                Clipboard.SetText(msg.Text ?? string.Empty);
            }
        }

        private void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ChatMessage msg)
            {
                msg.BeginEdit();
            }
        }

        private void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.CommandParameter is ChatMessage msg)
            {
                _viewModel.ChatMessages.Remove(msg);
            }
        }

        private void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is ChatMessage msg)
            {
                msg.SaveEdit();
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.CommandParameter is ChatMessage msg)
            {
                msg.CancelEdit();
            }
        }

    }
}