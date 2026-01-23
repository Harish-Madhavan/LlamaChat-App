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
using UglyToad.PdfPig;
using System.Text;
using System.Text.RegularExpressions;

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
        private readonly string _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaChatApp",
            "settings.json"
        );

        private readonly string _conversationsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaChatApp",
            "conversations.json"
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
            set { _isModelLoaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool IsIdle => IsModelLoaded && !IsGenerating;

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

        // Add import PDF command
        public ICommand ImportPdfCommand { get; }

        public Func<string?>? RequestFileDialog { get; set; }
        // add a public Func for PDF file dialog; assigned by the View
        public Func<string?>? RequestPdfFileDialog { get; set; }
        #endregion

        public MainViewModel()
        {
            // existing command initializations
            LoadSettingsCommand = new RelayCommand(async _ => await LoadAppSettingsAsync());
            SaveSettingsCommand = new RelayCommand(_ => SaveAppSettings());
            LoadModelCommand = new RelayCommand(async _ => await ExecuteLoadModelAsync(), _ => !IsGenerating);
            SendMessageCommand = new RelayCommand(async _ => await HandleUserPrompt(), _ => IsIdle && !string.IsNullOrWhiteSpace(UserInputText));
            StopGenerationCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsGenerating);
            ClearChatCommand = new RelayCommand(_ => ExecuteClearChat(), _ => IsModelLoaded && IsIdle);
            ExitCommand = new RelayCommand(_ => { Dispose(); Application.Current.Shutdown(); });

            // conversation commands
            NewConversationCommand = new RelayCommand(_ => CreateNewConversation());
            CloseConversationCommand = new RelayCommand(obj => CloseConversation(obj as ChatConversation), obj => obj is ChatConversation);
            SelectConversationCommand = new RelayCommand(obj => CurrentConversation = obj as ChatConversation, obj => obj is ChatConversation);
            RenameConversationCommand = new RelayCommand(obj => { /* optional: implement rename dialog */ }, obj => obj is ChatConversation);

            // Import PDF command
            ImportPdfCommand = new RelayCommand(async _ => await ExecuteImportPdfAsync());

            // initialize with a default conversation
            var conv = new ChatConversation { Title = "Conversation 1" };
            conv.Messages.Add(new ChatMessage { Author = "System", Text = "Welcome! Please open a GGUF model file from the File menu to begin.", AuthorBrush = Brushes.DarkSlateGray });
            Conversations.Add(conv);
            CurrentConversation = conv;
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

            // Load persisted conversations if any
            await LoadConversationsAsync();

            if (!string.IsNullOrEmpty(_settings.LastModelPath) && File.Exists(_settings.LastModelPath))
            {
                await LoadModelAsync(_settings.LastModelPath);
            }
            else
            {
                // If no conversations were loaded, ensure a welcome message exists in the default conversation
                if (Conversations.Count == 0)
                {
                    var conv = new ChatConversation { Title = "Conversation 1" };
                    conv.Messages.Add(new ChatMessage { Author = "System", Text = "Welcome! Please open a GGUF model file from the File menu to begin.", AuthorBrush = Brushes.DarkSlateGray });
                    Conversations.Add(conv);
                    CurrentConversation = conv;
                }
                else if (CurrentConversation == null && Conversations.Count > 0)
                {
                    CurrentConversation = Conversations[0];
                }
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
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            File.WriteAllText(_settingsFilePath, json);

            // Also save conversations
            try
            {
                SaveConversations();
            }
            catch { }
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

        private void CreateNewConversation()
        {
            var conv = new ChatConversation { Title = $"Conversation {Conversations.Count + 1}" };
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

        // New: create a summarized import that creates a single document entry and optional summary
        private async Task ExecuteImportPdfAsync()
        {
            var path = RequestPdfFileDialog?.Invoke();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                string fullText = string.Empty;
                await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    using var document = PdfDocument.Open(path);
                    foreach (var page in document.GetPages())
                    {
                        var text = page.Text;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine(text);
                        }
                    }
                    fullText = sb.ToString().Trim();
                });

                if (string.IsNullOrWhiteSpace(fullText))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var notice = new ChatConversation { Title = Path.GetFileName(path) };
                        notice.Messages.Add(new ChatMessage { Author = "System", Text = "No extractable text found in PDF.", AuthorBrush = Brushes.DarkSlateGray });
                        Conversations.Add(notice);
                        CurrentConversation = notice;
                        SaveConversations();
                    });
                    return;
                }

                // Clean and normalize whitespace
                fullText = Regex.Replace(fullText, "\r\n|\n|\r", "\n");
                fullText = Regex.Replace(fullText, "\n{2,}", "\n\n");

                // Build a document message containing the full text but keep it as a single message for reference
                var conv = new ChatConversation { Title = Path.GetFileName(path), SourcePath = path };
                conv.Messages.Add(new ChatMessage
                {
                    Author = "Document",
                    Text = fullText,
                    Alignment = HorizontalAlignment.Left,
                    AuthorBrush = Brushes.Gray,
                    BubbleBackground = new SolidColorBrush(Color.FromRgb(250, 250, 250))
                });

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Conversations.Add(conv);
                    CurrentConversation = conv;
                    SaveConversations();
                });

                // Generate a short summary in background (if model loaded use model, otherwise use heuristic)
                _ = Task.Run(async () =>
                {
                    string summary = await GeneratePdfSummaryAsync(fullText, Path.GetFileName(path));
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        conv.Summary = summary;
                        conv.Messages.Insert(0, new ChatMessage { Author = "Summary", Text = summary, AuthorBrush = Brushes.DarkSlateGray, BubbleBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245)) });
                        SaveConversations();
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> GeneratePdfSummaryAsync(string fullText, string title)
        {
            try
            {
                if (_session != null && IsModelLoaded)
                {
                    // Use the local model to create a short summary prompt
                    var prompt = $"Summarize the following document titled '{title}' into 5 concise bullet points:\n\n{(fullText.Length > 20000 ? fullText.Substring(0, 20000) : fullText)}";

                    // Send the prompt synchronously via the existing chat machinery
                    var tcs = new TaskCompletionSource<string>();
                    var summaryBuilder = new StringBuilder();

                    var assistantMessage = new ChatMessage { Author = "Assistant", Text = "▍", AuthorBrush = Brushes.DarkGreen };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentConversation?.Messages.Add(assistantMessage);
                    });

                    var localCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    try
                    {
                        await Task.Run(async () =>
                        {
                            var pipeline = new DefaultSamplingPipeline { Temperature = 0.2f, TopP = 0.9f, RepeatPenalty = 1.1f };
                            var inferenceParams = new InferenceParams
                            {
                                MaxTokens = 300,
                                SamplingPipeline = pipeline
                            };

                            var userMessage = new ChatHistory.Message(AuthorRole.User, prompt);
                            await foreach (var text in _session.ChatAsync(userMessage, inferenceParams, localCts.Token))
                            {
                                summaryBuilder.Append(text);
                            }

                            tcs.SetResult(summaryBuilder.ToString());
                        }, localCts.Token);

                        var result = await tcs.Task;
                        Application.Current.Dispatcher.Invoke(() => assistantMessage.Text = result);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() => assistantMessage.Text = $"[Summary error: {ex.Message}]" );
                        return $"[Summary failed: {ex.Message}]";
                    }
                    finally
                    {
                        localCts.Dispose();
                    }
                }
                else
                {
                    // Heuristic summary: take the first N sentences or first 500 chars
                    var sentences = Regex.Split(fullText, "(?<=[.!?])\\s+");
                    var take = Math.Min(5, sentences.Length);
                    var sb = new StringBuilder();
                    for (int i = 0; i < take; i++)
                    {
                        sb.AppendLine("- " + sentences[i].Trim());
                    }

                    if (sb.Length == 0)
                    {
                        var preview = fullText.Length > 500 ? fullText.Substring(0, 500) + "..." : fullText;
                        sb.AppendLine(preview);
                    }
                    return sb.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                return $"[Summary error: {ex.Message}]";
            }
        }

        #endregion

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                _cts?.Dispose();
            }
            catch { }
            _cts = null;

            try
            {
                _session = null;
            }
            catch { }

            try
            {
                _context?.Dispose();
            }
            catch { }
            _context = null;

            try
            {
                _model?.Dispose();
            }
            catch { }
            _model = null;

            // Save conversations before exiting
            try
            {
                SaveConversations();
            }
            catch { }

            // Ensure UI requery so buttons update after disposal
            CommandManager.InvalidateRequerySuggested();
            GC.SuppressFinalize(this);
        }
    }
}