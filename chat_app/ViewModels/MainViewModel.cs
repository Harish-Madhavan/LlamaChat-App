// ViewModels/MainViewModel.cs
using LLama;
using LLama.Common;
using LLama.Sampling;
using LlamaChatApp.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LlamaChatApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        #region Private Fields
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
        #endregion

        #region UI Properties
        private string _modelName = "No Model Loaded";
        public string ModelName
        {
            get => _modelName;
            set { _modelName = value; OnPropertyChanged(); }
        }

        private bool _isModelLoaded;
        public bool IsModelLoaded
        {
            get => _isModelLoaded;
            set { _isModelLoaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => IsModelLoaded && !IsGenerating;

        private string _userInputText = string.Empty;
        public string UserInputText
        {
            get => _userInputText;
            set { _userInputText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ChatMessage> ChatMessages { get; } = new ObservableCollection<ChatMessage>();
        #endregion

        #region Settings Properties
        private double _temperature;
        public double Temperature { get => _temperature; set { _temperature = value; OnPropertyChanged(); } }

        private double _topP;
        public double TopP { get => _topP; set { _topP = value; OnPropertyChanged(); } }

        private int _maxTokens;
        public int MaxTokens { get => _maxTokens; set { _maxTokens = value; OnPropertyChanged(); } }

        private int _gpuLayerCount;
        public int GpuLayerCount { get => _gpuLayerCount; set { _gpuLayerCount = value; OnPropertyChanged(); } }

        private int _contextSize;
        public int ContextSize { get => _contextSize; set { _contextSize = value; OnPropertyChanged(); } }

        private string _systemPrompt = string.Empty;
        public string SystemPrompt { get => _systemPrompt; set { _systemPrompt = value; OnPropertyChanged(); } }
        #endregion

        #region Commands
        public ICommand LoadSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand LoadModelCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand StopGenerationCommand { get; }
        public ICommand ClearChatCommand { get; }
        public ICommand ExitCommand { get; }

        public Func<string?>? RequestFileDialog { get; set; }
        #endregion

        public MainViewModel()
        {
            LoadSettingsCommand = new RelayCommand(async _ => await LoadAppSettingsAsync());
            SaveSettingsCommand = new RelayCommand(_ => SaveAppSettings());
            LoadModelCommand = new RelayCommand(async _ => await ExecuteLoadModelAsync(), _ => !IsGenerating);
            SendMessageCommand = new RelayCommand(async _ => await HandleUserPrompt(), _ => IsIdle && !string.IsNullOrWhiteSpace(UserInputText));
            StopGenerationCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsGenerating);
            ClearChatCommand = new RelayCommand(_ => ExecuteClearChat(), _ => IsModelLoaded && IsIdle);
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
        }

        #region Command Logic and Core Methods

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
                    MessageBox.Show($"Could not load settings: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _settings = new AppSettings();
                }
            }

            Temperature = _settings.Temperature;
            TopP = _settings.TopP;
            MaxTokens = _settings.MaxTokens;
            GpuLayerCount = _settings.GpuLayerCount;
            ContextSize = _settings.ContextSize;
            SystemPrompt = _settings.SystemPrompt;

            if (!string.IsNullOrEmpty(_settings.LastModelPath) && File.Exists(_settings.LastModelPath))
            {
                await LoadModelAsync(_settings.LastModelPath);
            }
            else
            {
                ChatMessages.Add(new ChatMessage
                {
                    Author = "System",
                    Text = "Welcome! Please open a GGUF model file from the File menu to begin.",
                    AuthorBrush = Brushes.DarkSlateGray
                });
            }
        }

        private void SaveAppSettings()
        {
            _settings.Temperature = Temperature;
            _settings.TopP = TopP;
            _settings.MaxTokens = MaxTokens;
            _settings.GpuLayerCount = GpuLayerCount;
            _settings.ContextSize = ContextSize;
            _settings.SystemPrompt = SystemPrompt;

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }

        private async Task ExecuteLoadModelAsync()
        {
            var modelPath = RequestFileDialog?.Invoke();
            if (!string.IsNullOrEmpty(modelPath))
            {
                await LoadModelAsync(modelPath);
            }
        }

        private async Task LoadModelAsync(string modelPath)
        {
            IsGenerating = true;
            IsModelLoaded = false;
            ChatMessages.Clear();
            ChatMessages.Add(new ChatMessage { Author = "System", Text = "Loading model, please wait...", AuthorBrush = Brushes.DarkSlateGray });

            try
            {
                await Task.Run(() =>
                {
                    _model?.Dispose();
                    _context?.Dispose();

                    var parameters = new ModelParams(modelPath)
                    {
                        ContextSize = (uint)ContextSize,
                        GpuLayerCount = GpuLayerCount
                    };
                    _model = LLamaWeights.LoadFromFile(parameters);

                    ResetContextAndChat();
                });

                _settings.LastModelPath = modelPath;
                ModelName = Path.GetFileName(modelPath);
                ChatMessages.Clear();
                ChatMessages.Add(new ChatMessage
                {
                    Author = "System",
                    Text = $"Model loaded successfully: **{ModelName}**\nYour system prompt has been applied. You can start chatting now!",
                    AuthorBrush = Brushes.DarkSlateGray
                });
                IsModelLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatMessages.Add(new ChatMessage { Author = "Error", Text = $"Could not load model.\nDetails: {ex.Message}", AuthorBrush = Brushes.Red });
                ModelName = "Load Failed";
                _settings.LastModelPath = null;
                _model = null;
                _context = null;
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void ExecuteClearChat()
        {
            ResetContextAndChat();
            ChatMessages.Clear();
            ChatMessages.Add(new ChatMessage { Author = "System", Text = "Chat cleared. The system prompt has been re-applied. You can start a new conversation.", AuthorBrush = Brushes.DarkSlateGray });
        }

        private void ResetContextAndChat()
        {
            if (_model == null || string.IsNullOrEmpty(_settings.LastModelPath)) return;

            _context?.Dispose();

            var parameters = new ModelParams(_settings.LastModelPath)
            {
                ContextSize = (uint)ContextSize,
                GpuLayerCount = GpuLayerCount
            };
            _context = _model.CreateContext(parameters);

            // --- FIX 1: This is the standard way to create a session with a system prompt ---
            var executor = new InteractiveExecutor(_context);
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.System, SystemPrompt);
            _session = new ChatSession(executor, history);
        }

        private async Task HandleUserPrompt()
        {
            if (_session == null) return;
            var prompt = UserInputText;
            UserInputText = string.Empty;
            IsGenerating = true;

            ChatMessages.Add(new ChatMessage
            {
                Author = "You",
                Text = prompt,
                Alignment = HorizontalAlignment.Right,
                AuthorBrush = Brushes.DodgerBlue,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(225, 245, 254))
            });

            var assistantMessage = new ChatMessage
            {
                Author = "Assistant",
                Text = "▍",
                Alignment = HorizontalAlignment.Left,
                AuthorBrush = Brushes.DarkGreen,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240))
            };
            ChatMessages.Add(assistantMessage);

            _cts = new CancellationTokenSource();
            bool firstToken = true;

            try
            {
                await Task.Run(async () =>
                {
                    var pipeline = new DefaultSamplingPipeline { Temperature = (float)Temperature, TopP = (float)TopP, RepeatPenalty = 1.1f };
                    var inferenceParams = new InferenceParams()
                    {
                        MaxTokens = MaxTokens,
                        AntiPrompts = new List<string> { "<|eot_id|>", "User:", "You:" },
                        SamplingPipeline = pipeline
                    };

                    // --- FIX 2 & 3: We must wrap the user's prompt in a Message object ---
                    // This resolves all the argument conversion errors.
                    var userMessage = new ChatHistory.Message(AuthorRole.User, prompt);

                    await foreach (var text in _session.ChatAsync(userMessage, inferenceParams, _cts.Token))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (firstToken) { assistantMessage.Text = ""; firstToken = false; }
                            assistantMessage.Text += text;
                        });
                    }
                }, _cts.Token);
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
                IsGenerating = false;
            }
        }
        #endregion
    }
}