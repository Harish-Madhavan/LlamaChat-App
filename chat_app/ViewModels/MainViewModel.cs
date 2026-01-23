// ViewModels/MainViewModel.cs
using LLama;
using LLama.Common;
using LLama.Sampling;
using LlamaChatApp.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LlamaChatApp.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        #region Private Fields
        private LLamaWeights? _model;
        private LLamaContext? _context;
        private ChatSession? _session;
        private CancellationTokenSource? _cts;

        private AppSettings _settings = new AppSettings();
        private readonly string _settingsFilePath = AppConstants.SETTINGS_FILE_PATH;
        private readonly string _conversationsFilePath = AppConstants.CONVERSATIONS_FILE_PATH;
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
            set { _isModelLoaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool IsIdle => IsModelLoaded && !IsGenerating;

        private AppLoadingState _loadingState = AppLoadingState.Idle;
        public AppLoadingState LoadingState
        {
            get => _loadingState;
            set { _loadingState = value; OnPropertyChanged(); }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private string _userInputText = string.Empty;
        public string UserInputText
        {
            get => _userInputText;
            set { _userInputText = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        // Replace single ChatMessages with multiple conversations
        public ObservableCollection<ChatConversation> Conversations { get; } = new ObservableCollection<ChatConversation>();

        private ChatConversation? _currentConversation;
        public ChatConversation? CurrentConversation
        {
            get => _currentConversation;
            set
            {
                _currentConversation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChatMessages));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        // Backwards-compatible property used by XAML
        public ObservableCollection<ChatMessage> ChatMessages => CurrentConversation?.Messages ?? _fallbackMessages;

        private readonly ObservableCollection<ChatMessage> _fallbackMessages = new ObservableCollection<ChatMessage>();
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

        // conversation commands
        public ICommand NewConversationCommand { get; }
        public ICommand CloseConversationCommand { get; }
        public ICommand RenameConversationCommand { get; }
        public ICommand SelectConversationCommand { get; }

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
            ExitCommand = new RelayCommand(_ => { Dispose(); Application.Current.Shutdown(); });

            NewConversationCommand = new RelayCommand(_ => CreateNewConversation());
            CloseConversationCommand = new RelayCommand(obj => CloseConversation(obj as ChatConversation), obj => obj is ChatConversation);
            SelectConversationCommand = new RelayCommand(obj => CurrentConversation = obj as ChatConversation, obj => obj is ChatConversation);
            RenameConversationCommand = new RelayCommand(obj => { /* optional: implement rename dialog */ }, obj => obj is ChatConversation);

            var conv = new ChatConversation { Title = AppConstants.FIRST_CONVERSATION_TITLE };
            conv.Messages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_WELCOME, AuthorBrush = Brushes.DarkSlateGray });
            Conversations.Add(conv);
            CurrentConversation = conv;
        }

        #region Command Logic and Core Methods

        private async Task LoadAppSettingsAsync()
        {
            LoadingState = AppLoadingState.LoadingSettings;
            StatusMessage = "Loading settings...";
            
            try
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
                        System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                        MessageBox.Show($"Could not load settings: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _settings = new AppSettings();
                    }
                }
                else
                {
                    _settings = new AppSettings();
                }

                ApplySettings(_settings);
                await LoadConversationsAsync();

                if (!string.IsNullOrEmpty(_settings.LastModelPath) && File.Exists(_settings.LastModelPath))
                {
                    await LoadModelAsync(_settings.LastModelPath);
                }
                else
                {
                    EnsureDefaultConversation();
                    StatusMessage = "Ready";
                }
            }
            finally
            {
                LoadingState = AppLoadingState.Idle;
            }
        }

        private void ApplySettings(AppSettings settings)
        {
            Temperature = settings.Temperature;
            TopP = settings.TopP;
            MaxTokens = settings.MaxTokens;
            GpuLayerCount = settings.GpuLayerCount;
            ContextSize = settings.ContextSize;
            SystemPrompt = settings.SystemPrompt;
        }

        private void EnsureDefaultConversation()
        {
            if (Conversations.Count == 0)
            {
                var conv = new ChatConversation { Title = AppConstants.FIRST_CONVERSATION_TITLE };
                conv.Messages.Add(new ChatMessage 
                { 
                    Author = AppConstants.ROLE_SYSTEM, 
                    Text = AppConstants.MESSAGE_WELCOME, 
                    AuthorBrush = Brushes.DarkSlateGray 
                });
                Conversations.Add(conv);
                CurrentConversation = conv;
            }
            else if (CurrentConversation == null && Conversations.Count > 0)
            {
                CurrentConversation = Conversations[0];
            }
        }

        private void SaveAppSettings()
        {
            try
            {
                UpdateSettingsFromUI();
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
                File.WriteAllText(_settingsFilePath, json);
                SaveConversations();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private void UpdateSettingsFromUI()
        {
            _settings.Temperature = Temperature;
            _settings.TopP = TopP;
            _settings.MaxTokens = MaxTokens;
            _settings.GpuLayerCount = GpuLayerCount;
            _settings.ContextSize = ContextSize;
            _settings.SystemPrompt = SystemPrompt;
        }

        private void SaveConversations()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_conversationsFilePath)!);

                var toSave = Conversations.Select(c => new SavedConversation
                {
                    Title = c.Title,
                    Messages = c.Messages.Select(m => new SavedMessage { Author = m.Author, Text = m.Text }).ToList()
                }).ToList();

                var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_conversationsFilePath, json);
            }
            catch (Exception ex)
            {
                // Fail silently but could log
                System.Diagnostics.Debug.WriteLine($"Failed to save conversations: {ex.Message}");
            }
        }

        private async Task LoadConversationsAsync()
        {
            try
            {
                if (File.Exists(_conversationsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_conversationsFilePath);
                    var saved = JsonSerializer.Deserialize<List<SavedConversation>>(json);
                    if (saved != null)
                    {
                        Conversations.Clear();
                        foreach (var sc in saved)
                        {
                            var conv = new ChatConversation { Title = sc.Title };
                            foreach (var sm in sc.Messages)
                            {
                                conv.Messages.Add(new ChatMessage { Author = sm.Author, Text = sm.Text, Alignment = HorizontalAlignment.Left });
                            }
                            Conversations.Add(conv);
                        }
                        CurrentConversation = Conversations.FirstOrDefault();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load conversations: {ex.Message}");
            }
        }

        // DTOs for persistence
        private class SavedConversation
        {
            public string Title { get; set; } = string.Empty;
            public List<SavedMessage> Messages { get; set; } = new List<SavedMessage>();
        }

        private class SavedMessage
        {
            public string Author { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
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
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                MessageBox.Show(AppConstants.MESSAGE_FILE_NOT_FOUND, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsGenerating = true;
            IsModelLoaded = false;
            LoadingState = AppLoadingState.LoadingModel;
            ChatMessages.Clear();
            ChatMessages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_LOADING, AuthorBrush = Brushes.DarkSlateGray });
            StatusMessage = "Loading model...";

            try
            {
                await Task.Run(() =>
                {
                    DisposeModel();
                    LoadModelInternal(modelPath);
                });

                _settings.LastModelPath = modelPath;
                ModelName = Path.GetFileName(modelPath);
                ChatMessages.Clear();
                ChatMessages.Add(new ChatMessage
                {
                    Author = AppConstants.ROLE_SYSTEM,
                    Text = string.Format(AppConstants.MESSAGE_LOADED_SUCCESS, ModelName),
                    AuthorBrush = Brushes.DarkSlateGray
                });
                IsModelLoaded = true;
                StatusMessage = $"Model ready: {ModelName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Model load error: {ex}");
                MessageBox.Show($"Failed to load model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatMessages.Add(new ChatMessage { Author = AppConstants.ROLE_ERROR, Text = string.Format(AppConstants.MESSAGE_LOAD_FAILED, ex.Message), AuthorBrush = Brushes.Red });
                ModelName = "Load Failed";
                _settings.LastModelPath = null;
                DisposeModel();
                StatusMessage = "Model load failed";
            }
            finally
            {
                IsGenerating = false;
                LoadingState = AppLoadingState.Idle;
            }
        }

        private void LoadModelInternal(string modelPath)
        {
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)ContextSize,
                GpuLayerCount = GpuLayerCount
            };
            _model = LLamaWeights.LoadFromFile(parameters);
            ResetContextAndChat();
        }

        private void DisposeModel()
        {
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
        }

        private void CreateNewConversation()
        {
            var conv = new ChatConversation { Title = string.Format(AppConstants.DEFAULT_CONVERSATION_TITLE, Conversations.Count + 1) };
            Conversations.Add(conv);
            CurrentConversation = conv;
        }

        private void CloseConversation(ChatConversation? conv)
        {
            if (conv == null) return;
            if (Conversations.Contains(conv))
            {
                Conversations.Remove(conv);
                if (CurrentConversation == conv)
                {
                    CurrentConversation = Conversations.Count > 0 ? Conversations[0] : null;
                }
            }
        }

        private void ExecuteClearChat()
        {
            ResetContextAndChat();
            ChatMessages.Clear();
            ChatMessages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_CHAT_CLEARED, AuthorBrush = Brushes.DarkSlateGray });
            StatusMessage = "Chat cleared";
        }

        private void ResetContextAndChat()
        {
            if (_model == null || string.IsNullOrEmpty(_settings.LastModelPath))
                return;

            _context?.Dispose();

            var parameters = new ModelParams(_settings.LastModelPath)
            {
                ContextSize = (uint)ContextSize,
                GpuLayerCount = GpuLayerCount
            };
            _context = _model.CreateContext(parameters);

            var executor = new InteractiveExecutor(_context);
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.System, SystemPrompt);
            _session = new ChatSession(executor, history);
        }

        private async Task HandleUserPrompt()
        {
            if (_session == null)
                return;

            var prompt = UserInputText;
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            UserInputText = string.Empty;
            IsGenerating = true;
            LoadingState = AppLoadingState.Generating;
            StatusMessage = "Generating response...";

            var userMessage = new ChatMessage
            {
                Author = AppConstants.ROLE_USER,
                Text = prompt,
                Alignment = HorizontalAlignment.Right,
                AuthorBrush = Brushes.DodgerBlue,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(AppConstants.Colors.USER_R, AppConstants.Colors.USER_G, AppConstants.Colors.USER_B))
            };
            ChatMessages.Add(userMessage);

            var assistantMessage = new ChatMessage
            {
                Author = AppConstants.ROLE_ASSISTANT,
                Text = "▍",
                Alignment = HorizontalAlignment.Left,
                AuthorBrush = Brushes.DarkGreen,
                BubbleBackground = new SolidColorBrush(Color.FromRgb(AppConstants.Colors.ASSISTANT_R, AppConstants.Colors.ASSISTANT_G, AppConstants.Colors.ASSISTANT_B))
            };
            ChatMessages.Add(assistantMessage);

            _cts = new CancellationTokenSource();
            bool firstToken = true;

            try
            {
                await Task.Run(async () => await GenerateResponseAsync(prompt, assistantMessage, firstToken), _cts.Token);
                StatusMessage = "Ready";
            }
            catch (OperationCanceledException)
            {
                assistantMessage.Text += AppConstants.MESSAGE_GENERATION_STOPPED;
                StatusMessage = "Generation stopped";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Inference error: {ex}");
                MessageBox.Show($"An error occurred during inference: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                assistantMessage.Text += string.Format(AppConstants.MESSAGE_GENERATION_ERROR, ex.Message);
                StatusMessage = "Generation error";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsGenerating = false;
                LoadingState = AppLoadingState.Idle;
            }
        }

        private async Task GenerateResponseAsync(string prompt, ChatMessage assistantMessage, bool firstToken)
        {
            var pipeline = new DefaultSamplingPipeline 
            { 
                Temperature = (float)Temperature, 
                TopP = (float)TopP, 
                RepeatPenalty = AppConstants.DEFAULT_REPEAT_PENALTY
            };
            
            var inferenceParams = new InferenceParams
            {
                MaxTokens = MaxTokens,
                AntiPrompts = AppConstants.ANTI_PROMPTS.ToList(),
                SamplingPipeline = pipeline
            };

            var userMessage = new ChatHistory.Message(AuthorRole.User, prompt);

            await foreach (var text in _session.ChatAsync(userMessage, inferenceParams, _cts.Token))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (firstToken) 
                    { 
                        assistantMessage.Text = ""; 
                        firstToken = false; 
                    }
                    assistantMessage.Text += text;
                });
            }
        }

        #endregion

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing cancellation token: {ex.Message}");
            }

            _cts = null;
            _session = null;

            try
            {
                DisposeModel();
                SaveConversations();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disposal: {ex.Message}");
            }

            CommandManager.InvalidateRequerySuggested();
            GC.SuppressFinalize(this);
        }
    }
}