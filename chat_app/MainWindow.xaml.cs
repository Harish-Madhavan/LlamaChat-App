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

namespace LlamaChatApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private INotifyCollectionChanged? _subscribedChatMessages;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        this.Loaded += MainWindow_Loaded;
        this.Closing += MainWindow_Closing;

        // Subscribe to property changes on the VM so we can react to ChatMessages swapping
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        // Subscribe to the initial ChatMessages collection (if any)
        SubscribeToChatMessages(_viewModel.ChatMessages);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ChatMessages))
        {
            // ChatMessages collection instance changed — update subscription to avoid duplicates
            SubscribeToChatMessages(_viewModel.ChatMessages);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LoadSettingsCommand.CanExecute(null))
        {
            _viewModel.LoadSettingsCommand.Execute(null);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.SaveSettingsCommand.CanExecute(null))
        {
            _viewModel.SaveSettingsCommand.Execute(null);
        }
        // Unsubscribe handlers to avoid leaks
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        SubscribeToChatMessages(null);

        _viewModel.Dispose();
    }

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom(force: true);
        }
    }

    private void SubscribeToChatMessages(INotifyCollectionChanged? collection)
    {
        if (_subscribedChatMessages != null)
        {
            _subscribedChatMessages.CollectionChanged -= ChatMessages_CollectionChanged;
            _subscribedChatMessages = null;
        }

        if (collection != null)
        {
            _subscribedChatMessages = collection;
            _subscribedChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
        }
    }

    private void ChatListView_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_viewModel.IsGenerating)
        {
            ScrollToBottom(force: false);
        }
    }

    private void ScrollToBottom(bool force = false)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel.ChatMessages.Count == 0) return;

            if (VisualTreeHelper.GetChild(ChatListView, 0) is Decorator border)
            {
                var scrollViewer = border.Child as ScrollViewer;
                if (scrollViewer != null)
                {
                    if (force || scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 150)
                    {
                        ChatListView.ScrollIntoView(_viewModel.ChatMessages.Last());
                    }
                }
            }
        }));
    }

    private void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.Shift)
            {
                return;
            }

            e.Handled = true;
            if (_viewModel.SendMessageCommand.CanExecute(null))
            {
                _viewModel.SendMessageCommand.Execute(null);
                InputTextBox.Focus();
            }
        }
    }

    private void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ChatMessage msg)
        {
            msg.BeginEdit();
        }
    }

    private void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.CommandParameter is ChatMessage msg)
        {
            msg.SaveEdit();
            if (_viewModel.MessageEditedCommand.CanExecute(msg))
            {
                _viewModel.MessageEditedCommand.Execute(msg);
            }
        }
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.CommandParameter is ChatMessage msg)
        {
            msg.CancelEdit();
        }
    }

    private void ChatBubble_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var menu = (ContextMenu)this.Resources["MessageContextMenu"];
            if (menu != null)
            {
                menu.PlacementTarget = fe;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();
    }
}
