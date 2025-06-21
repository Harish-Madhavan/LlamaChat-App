using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LlamaChatApp
{
    public partial class MainWindow : Window
    {
        private LLamaWeights? _model;
        private LLamaContext? _context;
        private ChatSession? _session;
        private CancellationTokenSource? _cts;

        private AppSettings _settings = new AppSettings();
        private readonly string _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaChatApp",
            "settings.json"
        );

        private readonly ObservableCollection<ChatMessage> _chatMessages = new ObservableCollection<ChatMessage>();

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            ChatListView.ItemsSource = _chatMessages;
        }

        #region Lifecycle and Settings

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAppSettingsAsync();

            if (string.IsNullOrEmpty(_settings.LastModelPath))
            {
                _chatMessages.Add(new ChatMessage
                {
                    Author = "System",
                    Text = "Welcome! Please open a GGUF model file from the File menu to begin.",
                    AuthorBrush = Brushes.DarkSlateGray
                });
            }
            SetUiState(isModelLoaded: false, isGenerating: false);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveAppSettings();
            base.OnClosing(e);
        }

        private async Task LoadAppSettingsAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    // Handle potential file corruption
                    MessageBox.Show($"Could not load settings: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _settings = new AppSettings();
                }
            }

            TemperatureSlider.Value = _settings.Temperature;
            TopPSlider.Value = _settings.TopP;
            MaxTokensTextBox.Text = _settings.MaxTokens.ToString();

            if (!string.IsNullOrEmpty(_settings.LastModelPath) && File.Exists(_settings.LastModelPath))
            {
                await LoadModelAsync(_settings.LastModelPath);
            }
        }

        private void SaveAppSettings()
        {
            _settings.Temperature = TemperatureSlider.Value;
            _settings.TopP = TopPSlider.Value;
            _settings.MaxTokens = int.TryParse(MaxTokensTextBox.Text, out var mt) ? mt : 512;

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }

        #endregion

        #region UI Event Handlers

        private async void SendButton_Click(object sender, RoutedEventArgs e) => await HandleUserPrompt();

        private async void UserInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                e.Handled = true;
                await HandleUserPrompt();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void OpenModelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "GGUF Model Files (*.gguf)|*.gguf|All files (*.*)|*.*",
                Title = "Select a Llama Model File"
            };

            if (openFileDialog.ShowDialog() == true) await LoadModelAsync(openFileDialog.FileName);
        }

        private void ClearChatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null || _context == null) return;

            var executor = new InteractiveExecutor(_context);
            var chatHistory = new ChatHistory();
            chatHistory.AddMessage(AuthorRole.System, "You are a helpful, kind, and honest assistant named Bob. You always provide clear and concise answers.");
            _session = new ChatSession(executor, chatHistory);

            _chatMessages.Clear();
            _chatMessages.Add(new ChatMessage { Author = "System", Text = "Chat cleared. You can start a new conversation.", AuthorBrush = Brushes.DarkSlateGray });
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        #endregion

        #region Core Logic

        private void SetUiState(bool isModelLoaded, bool isGenerating)
        {
            OpenModelMenuItem.IsEnabled = !isGenerating;
            ClearChatMenuItem.IsEnabled = isModelLoaded && !isGenerating;
            UserInputTextBox.IsEnabled = isModelLoaded && !isGenerating;
            SendButton.IsEnabled = isModelLoaded && !isGenerating;
            StopButton.IsEnabled = isModelLoaded && isGenerating;
            SettingsPanel.IsEnabled = isModelLoaded && !isGenerating;
        }

        private async Task LoadModelAsync(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                MessageBox.Show($"Model file not found: {modelPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _settings.LastModelPath = null; // Clear invalid path from settings
                return;
            }

            _settings.LastModelPath = modelPath; // Store valid path
            SetUiState(isModelLoaded: false, isGenerating: false);
            _chatMessages.Clear();
            _chatMessages.Add(new ChatMessage { Author = "System", Text = "Loading model, please wait...", AuthorBrush = Brushes.DarkSlateGray });

            try
            {
                await Task.Run(() =>
                {
                    _context?.Dispose(); _model?.Dispose();
                    var parameters = new ModelParams(modelPath) { ContextSize = 2048, GpuLayerCount = 30 };
                    _model = LLamaWeights.LoadFromFile(parameters);
                    _context = _model.CreateContext(parameters);
                    var executor = new InteractiveExecutor(_context);
                    var chatHistory = new ChatHistory();
                    chatHistory.AddMessage(AuthorRole.System, "You are a helpful, kind, and honest assistant named Bob. You always provide clear and concise answers.");
                    _session = new ChatSession(executor, chatHistory);
                });

                _chatMessages.Clear();
                _chatMessages.Add(new ChatMessage
                {
                    Author = "System",
                    Text = $"Model loaded successfully: **{Path.GetFileName(modelPath)}**\nYou can start chatting now!",
                    AuthorBrush = Brushes.DarkSlateGray
                });
                SetUiState(isModelLoaded: true, isGenerating: false);
                UserInputTextBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _chatMessages.Add(new ChatMessage { Author = "Error", Text = $"Could not load model.\nDetails: {ex.Message}", AuthorBrush = Brushes.Red });
                SetUiState(isModelLoaded: false, isGenerating: false);
            }
        }

        private async Task HandleUserPrompt()
        {
            if (_session == null) return;
            var prompt = UserInputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;

            var temperature = (float)TemperatureSlider.Value;
            var topP = (float)TopPSlider.Value;
            var maxTokens = int.TryParse(MaxTokensTextBox.Text, out var mt) ? mt : 512;

            UserInputTextBox.Clear();
            SetUiState(isModelLoaded: true, isGenerating: true);

            _chatMessages.Add(new ChatMessage
            {
                Author = "You",
                Text = prompt,
                Alignment = HorizontalAlignment.Right,
                AuthorBrush = Brushes.DodgerBlue,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(225, 245, 254))
            });
            ChatListView.ScrollIntoView(_chatMessages[^1]);

            var assistantMessage = new ChatMessage
            {
                Author = "Assistant",
                Text = "▍",
                Alignment = HorizontalAlignment.Left,
                AuthorBrush = Brushes.DarkGreen,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240))
            };
            _chatMessages.Add(assistantMessage);
            ChatListView.ScrollIntoView(assistantMessage);

            _cts = new CancellationTokenSource();
            bool firstToken = true;

            try
            {
                await Task.Run(async () =>
                {
                    var pipeline = new DefaultSamplingPipeline { Temperature = temperature, TopP = topP, RepeatPenalty = 1.1f };
                    var inferenceParams = new InferenceParams()
                    {
                        MaxTokens = maxTokens,
                        AntiPrompts = new List<string> { "<|eot_id|>", "User:", "You:" },
                        SamplingPipeline = pipeline
                    };
                    var userMessage = new ChatHistory.Message(AuthorRole.User, prompt);

                    await foreach (var text in _session.ChatAsync(userMessage, inferenceParams, _cts.Token))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (firstToken) { assistantMessage.Text = ""; firstToken = false; }
                            assistantMessage.Text += text;
                            ChatListView.ScrollIntoView(assistantMessage);
                        });
                    }
                });
            }
            catch (OperationCanceledException) { assistantMessage.Text += " [generation stopped]"; }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during inference: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                assistantMessage.Text += $"\n[Error: {ex.Message}]";
            }
            finally
            {
                _cts.Dispose(); _cts = null;
                SetUiState(isModelLoaded: true, isGenerating: false);
                UserInputTextBox.Focus();
            }
        }

        #endregion
    }
}