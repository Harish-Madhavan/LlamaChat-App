using LLama.Common;
using LlamaChatApp.Commands;
using LlamaChatApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LlamaChatApp.ViewModels;
public class MainViewModel : ViewModelBase, IDisposable
{
    #region Private Fields
    private readonly ILLamaService _llamaService;
    private readonly IPdfService _pdfService;
    private readonly IDialogService _dialogService;
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

    private double _tps;
    public double TPS
    {
        get => _tps;
        set { _tps = value; OnPropertyChanged(); }
    }

    private string _userInputText = string.Empty;
    public string UserInputText
    {
        get => _userInputText;
        set { _userInputText = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public ObservableCollection<ChatConversation> Conversations { get; } = new ObservableCollection<ChatConversation>();

    private ChatConversation? _currentConversation;
    public ChatConversation? CurrentConversation
    {
        get => _currentConversation;
        set
        {
            if (_currentConversation != value)
            {
                _currentConversation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChatMessages));
                CommandManager.InvalidateRequerySuggested();

                if (_currentConversation != null && IsModelLoaded)
                {
                    RestoreContextFromConversation(_currentConversation);
                    UpdateContextUsage();
                }
            }
        }
    }

    public ObservableCollection<ChatMessage> ChatMessages => CurrentConversation?.Messages ?? _fallbackMessages;

    private readonly ObservableCollection<ChatMessage> _fallbackMessages = new ObservableCollection<ChatMessage>();

    private string _contextUsageString = "0 / 0";
    public string ContextUsageString
    {
        get => _contextUsageString;
        set { _contextUsageString = value; OnPropertyChanged(); }
    }

    private Brush _contextUsageBrush = Brushes.Gray;
    public Brush ContextUsageBrush
    {
        get => _contextUsageBrush;
        set { _contextUsageBrush = value; OnPropertyChanged(); }
    }
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

    public ICommand NewConversationCommand { get; }
    public ICommand CloseConversationCommand { get; }
    public ICommand RenameConversationCommand { get; }
    public ICommand SelectConversationCommand { get; }
    public ICommand SwitchToLightThemeCommand { get; }
    public ICommand SwitchToDarkThemeCommand { get; }
    public ICommand ExportConversationCommand { get; }
    public ICommand ImportConversationCommand { get; }
    public ICommand RegenerateResponseCommand { get; }
    public ICommand MessageEditedCommand { get; }
    public ICommand DeleteMessageCommand { get; }
    public ICommand CopyMessageCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand AttachPdfCommand { get; }
    public ICommand DeleteDocumentCommand { get; }
    public ICommand UnloadModelCommand { get; }
    public ICommand ReloadModelCommand { get; }
    #endregion

    public MainViewModel(ILLamaService llamaService, IPdfService pdfService, IDialogService dialogService)
    {
        _llamaService = llamaService;
        _pdfService = pdfService;
        _dialogService = dialogService;

        LoadSettingsCommand = new RelayCommand(async _ => await LoadAppSettingsAsync());      
        SaveSettingsCommand = new RelayCommand(_ => SaveAppSettings());
        LoadModelCommand = new RelayCommand(async _ => await ExecuteLoadModelAsync(), _ => !IsGenerating);
        UnloadModelCommand = new RelayCommand(_ => ExecuteUnloadModel(), _ => IsModelLoaded && !IsGenerating);
        ReloadModelCommand = new RelayCommand(async _ => await ExecuteReloadModelAsync(), _ => !IsGenerating);
        SendMessageCommand = new RelayCommand(async _ => await HandleUserPrompt(), _ => IsIdle && !string.IsNullOrWhiteSpace(UserInputText));
        StopGenerationCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsGenerating);     
        ClearChatCommand = new RelayCommand(_ => ExecuteClearChat(), _ => IsModelLoaded && IsIdle);
        ExitCommand = new RelayCommand(_ => { Dispose(); Application.Current.Shutdown(); });  

        NewConversationCommand = new RelayCommand(_ => CreateNewConversation());
        CloseConversationCommand = new RelayCommand(obj => CloseConversation(obj as ChatConversation), obj => obj is ChatConversation);
        SelectConversationCommand = new RelayCommand(obj => CurrentConversation = obj as ChatConversation, obj => obj is ChatConversation);
        RenameConversationCommand = new RelayCommand(obj => { }, obj => obj is ChatConversation);
        SwitchToLightThemeCommand = new RelayCommand(_ => ChangeTheme("Themes/LightTheme.xaml"));
        SwitchToDarkThemeCommand = new RelayCommand(_ => ChangeTheme("Themes/DarkTheme.xaml"));
        ExportConversationCommand = new RelayCommand(_ => ExportConversation(), _ => CurrentConversation != null);
        ImportConversationCommand = new RelayCommand(_ => ImportConversation());
        RegenerateResponseCommand = new RelayCommand(async _ => await RegenerateResponse(), _ => IsIdle && IsModelLoaded && ChatMessages.Count > 0);        
        MessageEditedCommand = new RelayCommand(_ =>
        {
            if (CurrentConversation != null && IsModelLoaded)
            {
                InvalidateCurrentCache();
                RestoreContextFromConversation(CurrentConversation);
            }
        }, _ => IsIdle && IsModelLoaded);     

        DeleteMessageCommand = new RelayCommand(obj =>
        {
            if (obj is ChatMessage msg && ChatMessages.Contains(msg))
            {
                ChatMessages.Remove(msg);     
                if (CurrentConversation != null && IsModelLoaded)
                {
                    InvalidateCurrentCache();
                    RestoreContextFromConversation(CurrentConversation);
                }
            }
        }, _ => IsIdle);

        CopyMessageCommand = new RelayCommand(obj =>
        {
            if (obj is ChatMessage msg && !string.IsNullOrEmpty(msg.Text))
            {
                try
                {
                    Clipboard.SetText(msg.Text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
                }
            }
        });

        CopyCodeCommand = new RelayCommand(obj =>
        {
            if (obj is string code && !string.IsNullOrEmpty(code))
            {
                try
                {
                    Clipboard.SetText(code);
                    StatusMessage = "Code copied to clipboard";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}");
                }
            }
        });

        AttachPdfCommand = new RelayCommand(async _ => await ExecuteAttachPdfAsync(), _ => IsIdle);

        DeleteDocumentCommand = new RelayCommand(obj =>
        {
            if (obj is AttachedDocument doc && CurrentConversation != null && CurrentConversation.AttachedDocuments.Contains(doc))
            {
                CurrentConversation.AttachedDocuments.Remove(doc);
                InvalidateCurrentCache();
                try
                {
                    string filePath = Path.Combine("Data", "Documents", $"{doc.Id}.txt");
                    if (File.Exists(filePath)) File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete offloaded document file: {ex}");
                }
                StatusMessage = $"Removed document: {doc.FileName}";
            }
        });

        var conv = new ChatConversation { Title = AppConstants.FIRST_CONVERSATION_TITLE };            
        conv.Messages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_WELCOME, AuthorBrush = Brushes.DarkSlateGray });
        Conversations.Add(conv);
        CurrentConversation = conv;
        UpdateContextUsage();
    }

    private void ChangeTheme(string themePath)
    {
        try
        {
            var appResources = Application.Current.Resources;
            var mergedDicts = appResources.MergedDictionaries;
            var existingTheme = mergedDicts.FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("Themes/", StringComparison.OrdinalIgnoreCase)); 

            var uriString = $"pack://application:,,,/{themePath}";
            var newTheme = new ResourceDictionary { Source = new Uri(uriString, UriKind.Absolute) };

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (existingTheme != null) mergedDicts.Remove(existingTheme);
                mergedDicts.Add(newTheme);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to change theme: {ex}");
        }
    }

    private void ExportConversation()
    {
        if (CurrentConversation == null) return;
        var filename = _dialogService.ShowSaveFileDialog("JSON Files (*.json)|*.json", "Export Conversation");
        if (string.IsNullOrEmpty(filename)) return;

        try
        {
            var toSave = new SavedConversation
            {
                Title = CurrentConversation.Title,
                Messages = CurrentConversation.Messages.Select(m => new SavedMessage { Author = m.Author, Text = m.Text }).ToList()
            };
            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filename, json);
            StatusMessage = "Conversation exported";
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog($"Failed to export: {ex.Message}");
        }
    }

    private void ImportConversation()
    {
        var filename = _dialogService.ShowOpenFileDialog("JSON Files (*.json)|*.json", "Import Conversation");
        if (string.IsNullOrEmpty(filename)) return;

        try
        {
            var json = File.ReadAllText(filename);
            var saved = JsonSerializer.Deserialize<SavedConversation>(json);

            if (saved != null)
            {
                var conv = new ChatConversation { Title = saved.Title };
                foreach (var sm in saved.Messages)
                {
                    conv.Messages.Add(new ChatMessage { Author = sm.Author, Text = sm.Text, Alignment = sm.Author == AppConstants.ROLE_USER ? HorizontalAlignment.Right : HorizontalAlignment.Left });
                }
                Conversations.Add(conv);
                CurrentConversation = conv;
                StatusMessage = "Conversation imported";
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog($"Failed to import: {ex.Message}");
        }
    }

    private void InvalidateCurrentCache()
    {
        if (CurrentConversation == null) return;
        try
        {
            string cachePath = Path.Combine("Data", "Cache", $"{CurrentConversation.Id}.bin");
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete cache file: {ex}");
        }
    }

    private void PruneOldCacheFiles()
    {
        try
        {
            string cacheDir = Path.Combine("Data", "Cache");
            if (!Directory.Exists(cacheDir)) return;

            var files = Directory.GetFiles(cacheDir, "*.bin");
            var cutOffDate = DateTime.Now.AddDays(-7);
            int deletedCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var lastWrite = File.GetLastWriteTime(file);
                    if (lastWrite < cutOffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to prune cache file {file}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Pruned {deletedCount} expired KV cache files.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cache pruning failed: {ex.Message}");
        }
    }

    private async Task RegenerateResponse(object? parameter = null)
    {
        if (CurrentConversation == null) return;

        ChatMessage? targetMessage = parameter as ChatMessage ?? ChatMessages.LastOrDefault();
        if (targetMessage == null) return;

        string prompt = string.Empty;

        if (targetMessage.Author == AppConstants.ROLE_ASSISTANT)
        {
            int index = ChatMessages.IndexOf(targetMessage);
            if (index <= 0) return; 

            var userMsg = ChatMessages[index - 1];
            if (userMsg.Author != AppConstants.ROLE_USER) return;

            prompt = userMsg.Text;
            while (ChatMessages.Count > index) ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Remove(userMsg);
        }
        else if (targetMessage.Author == AppConstants.ROLE_USER)
        {
            int index = ChatMessages.IndexOf(targetMessage);
            if (index == -1) return;

            prompt = targetMessage.Text;
            while (ChatMessages.Count > index + 1) ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Remove(targetMessage);
        }
        else return;

        InvalidateCurrentCache();
        UserInputText = prompt;
        RestoreContextFromConversation(CurrentConversation);
        await HandleUserPrompt();
    }

    #region Command Logic and Core Methods

    private async Task LoadAppSettingsAsync()
    {
        LoadingState = AppLoadingState.LoadingSettings;
        StatusMessage = "Loading settings...";

        try
        {
            var settingsDir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(settingsDir)) Directory.CreateDirectory(settingsDir);   
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    _dialogService.ShowErrorDialog($"Could not load settings: {ex.Message}");
                    _settings = new AppSettings();
                }
            }
            ApplySettings(_settings);
            await LoadConversationsAsync();
            PruneOldCacheFiles();

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
            conv.Messages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_WELCOME, AuthorBrush = Brushes.DarkSlateGray });
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
            var settingsDir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(settingsDir)) Directory.CreateDirectory(settingsDir);   
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
            var convDir = Path.GetDirectoryName(_conversationsFilePath);
            if (!string.IsNullOrEmpty(convDir)) Directory.CreateDirectory(convDir);

            var toSave = Conversations.Select(c => new SavedConversation
            {
                Id = c.Id,
                Title = c.Title,
                LastUsedModelPath = c.LastUsedModelPath,
                Messages = c.Messages
                    .Where(m => !m.Text.StartsWith("[System Context", StringComparison.OrdinalIgnoreCase))
                    .Select(m => new SavedMessage { Author = m.Author, Text = m.Text }).ToList(),
                AttachedDocuments = c.AttachedDocuments.ToList()
            }).ToList();

            var json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_conversationsFilePath, json);
        }
        catch (Exception ex)
        {
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
                        var conv = new ChatConversation 
                        { 
                            Id = string.IsNullOrEmpty(sc.Id) ? Guid.NewGuid().ToString("N") : sc.Id,
                            Title = sc.Title,
                            LastUsedModelPath = sc.LastUsedModelPath
                        };
                        foreach (var sm in sc.Messages)
                        {
                            var alignment = sm.Author == AppConstants.ROLE_USER ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                            var authorBrush = sm.Author == AppConstants.ROLE_USER ? Brushes.DodgerBlue : 
                                              sm.Author == AppConstants.ROLE_ASSISTANT ? Brushes.DarkGreen : Brushes.DarkSlateGray;
                            var bubbleBg = sm.Author == AppConstants.ROLE_USER 
                                ? new SolidColorBrush(Color.FromRgb(AppConstants.Colors.USER_R, AppConstants.Colors.USER_G, AppConstants.Colors.USER_B))
                                : new SolidColorBrush(Color.FromRgb(AppConstants.Colors.ASSISTANT_R, AppConstants.Colors.ASSISTANT_G, AppConstants.Colors.ASSISTANT_B));

                            conv.Messages.Add(new ChatMessage 
                            { 
                                Author = sm.Author, 
                                Text = sm.Text, 
                                Alignment = alignment,
                                AuthorBrush = authorBrush,
                                BubbleBackground = bubbleBg
                            });
                        }
                        if (sc.AttachedDocuments != null)
                        {
                            foreach (var doc in sc.AttachedDocuments)
                            {
                                conv.AttachedDocuments.Add(doc);
                            }
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

    private class SavedConversation
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? LastUsedModelPath { get; set; }
        public List<SavedMessage> Messages { get; set; } = new List<SavedMessage>();
        public List<AttachedDocument> AttachedDocuments { get; set; } = new List<AttachedDocument>();
    }

    private class SavedMessage
    {
        public string Author { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    private async Task ExecuteLoadModelAsync()
    {
        var modelPath = _dialogService.ShowOpenFileDialog("GGUF Model Files (*.gguf)|*.gguf|All files (*.*)|*.*", "Select a Llama Model File");
        if (!string.IsNullOrEmpty(modelPath))
        {
            await LoadModelAsync(modelPath);
        }
    }

    private void ExecuteUnloadModel()
    {
        try
        {
            _llamaService.UnloadModel();
            IsModelLoaded = false;
            ModelName = "No Model Loaded";
            _settings.LastModelPath = null;
            
            StatusMessage = "Model unloaded";
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog($"Failed to unload model: {ex.Message}");
            StatusMessage = "Model unload failed";
        }
    }

    private async Task ExecuteReloadModelAsync()
    {
        string? path = _llamaService.IsModelLoaded ? _llamaService.ModelPath : _settings.LastModelPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _dialogService.ShowErrorDialog("No loaded or previously selected model to reload.");
            return;
        }

        await LoadModelAsync(path);
    }

    private async Task LoadModelAsync(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath)) return;

        // Validate model settings first
        if (ContextSize <= 0)
        {
            _dialogService.ShowErrorDialog("Context Size must be a positive integer.");
            return;
        }
        if (GpuLayerCount < 0)
        {
            _dialogService.ShowErrorDialog("GPU Layer Count cannot be negative.");
            return;
        }

        IsGenerating = true;
        IsModelLoaded = false;
        LoadingState = AppLoadingState.LoadingModel;
        StatusMessage = "Loading model...";

        try
        {
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)ContextSize,
                GpuLayerCount = GpuLayerCount
            };

            await _llamaService.LoadModelAsync(modelPath, parameters);

            _settings.LastModelPath = modelPath;
            ModelName = _llamaService.ModelName;

            // Initialize a session with the system prompt
            _llamaService.InitializeSession(SystemPrompt);

            IsModelLoaded = true;
            StatusMessage = $"Model ready: {ModelName}";
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog($"Failed to load model: {ex.Message}");
            ChatMessages.Add(new ChatMessage { Author = AppConstants.ROLE_ERROR, Text = string.Format(AppConstants.MESSAGE_LOAD_FAILED, ex.Message), AuthorBrush = Brushes.Red });
            ModelName = "Load Failed";
            _settings.LastModelPath = null;
            StatusMessage = "Model load failed";
        }
        finally
        {
            IsGenerating = false;
            LoadingState = AppLoadingState.Idle;
        }
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
            // Delete offloaded document files for all attached documents in the conversation
            foreach (var doc in conv.AttachedDocuments)
            {
                try
                {
                    string filePath = Path.Combine("Data", "Documents", $"{doc.Id}.txt");
                    if (File.Exists(filePath)) File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete offloaded document file: {ex}");
                }
            }

            // We temporally set current to the closing one so Invalidate deletes it
            var tempCurrent = CurrentConversation;
            CurrentConversation = conv;
            InvalidateCurrentCache();
            CurrentConversation = tempCurrent;

            Conversations.Remove(conv);
            if (CurrentConversation == conv)
            {
                CurrentConversation = Conversations.Count > 0 ? Conversations[0] : null;      
            }
        }
    }

    private async Task ExecuteAttachPdfAsync()
    {
        if (CurrentConversation == null) return;

        var filename = _dialogService.ShowOpenFileDialog("PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*", "Select a PDF to attach");
        if (string.IsNullOrEmpty(filename)) return;

        LoadingState = AppLoadingState.Generating;
        StatusMessage = "Extracting text from PDF...";
        try
        {
            var text = await _pdfService.ExtractTextAsync(filename);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var doc = new AttachedDocument
                {
                    FileName = Path.GetFileName(filename),
                    FullPath = filename,
                    ExtractedText = text,
                    FileSize = new FileInfo(filename).Length
                };
                
                // Offload text to Data/Documents/{doc.Id}.txt
                try
                {
                    string docDir = Path.Combine("Data", "Documents");
                    Directory.CreateDirectory(docDir);
                    string docPath = Path.Combine(docDir, $"{doc.Id}.txt");
                    await File.WriteAllTextAsync(docPath, text);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write offloaded document file: {ex.Message}");
                }

                CurrentConversation.AttachedDocuments.Add(doc);

                var msg = new ChatMessage
                {
                    Author = AppConstants.ROLE_SYSTEM,
                    Text = $"📎 Attached Document: **{doc.FileName}** ({text.Length} characters)",
                    AuthorBrush = Brushes.DarkSlateGray
                };
                ChatMessages.Add(msg);
                StatusMessage = $"PDF '{doc.FileName}' attached.";
                
                InvalidateCurrentCache();
            }
            else
            {
                StatusMessage = "PDF was empty or could not be read.";
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog($"Failed to read PDF: {ex.Message}");
            StatusMessage = "PDF read failed.";
        }
        finally
        {
            LoadingState = AppLoadingState.Idle;
        }
    }

    private void ExecuteClearChat()
    {
        if (CurrentConversation == null) return;
        
        CurrentConversation.Messages.Clear();
        CurrentConversation.Messages.Add(new ChatMessage { Author = AppConstants.ROLE_SYSTEM, Text = AppConstants.MESSAGE_CHAT_CLEARED, AuthorBrush = Brushes.DarkSlateGray });

        InvalidateCurrentCache();
        
        // Re-initialize session
        try 
        {
            _llamaService.InitializeSession(SystemPrompt);
            StatusMessage = "Chat cleared";
        }
        catch (Exception ex)
        {
            StatusMessage = "Error clearing chat";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void RestoreContextFromConversation(ChatConversation conversation)
    {
        if (!_llamaService.IsModelLoaded) return;

        try
        {
            string cachePath = Path.Combine("Data", "Cache", $"{conversation.Id}.bin");
            
            // Proactively estimate token usage of the history
            int estimatedTokens = 0;
            estimatedTokens += _llamaService.CountTokens(SystemPrompt);
            foreach (var m in conversation.Messages)
            {
                if (m.Author == AppConstants.ROLE_USER || m.Author == AppConstants.ROLE_ASSISTANT)
                {
                    estimatedTokens += _llamaService.CountTokens(m.Text);
                }
            }

            // If history is small enough AND model matches AND cache file exists, try to load cache
            if (estimatedTokens < (ContextSize - MaxTokens - 200) && 
                conversation.LastUsedModelPath == _llamaService.ModelPath && 
                File.Exists(cachePath))
            {
                try
                {
                    var cachedHistory = GetPrunedHistory(conversation);
                    _llamaService.LoadState(cachePath, SystemPrompt, cachedHistory);
                    StatusMessage = "Context restored from KV cache (Instant)";
                    UpdateContextUsage();
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load KV cache: {ex}");
                    // Fallback to history replay
                }
            }

            // Fallback / Shifting memory window: Replay pruned history
            var history = GetPrunedHistory(conversation);
            _llamaService.RestoreSession(SystemPrompt, history);
            StatusMessage = "Context restored from history (Replayed)";
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error restoring context: {ex}");
            StatusMessage = "Context restore error";
        }
    }

    private IEnumerable<(string Author, string Text)> GetPrunedHistory(ChatConversation conversation, int newPromptTokens = 0)
    {
        var history = new List<(string Author, string Text)>();
        if (conversation == null) return history;

        int systemPromptTokens = _llamaService.CountTokens(SystemPrompt);
        int maxAllowedTokens = ContextSize - MaxTokens - 200; // Leave buffer for response
        if (maxAllowedTokens < 150) maxAllowedTokens = 150; // Safeguard minimum context

        int currentTotal = systemPromptTokens + newPromptTokens;

        // Iterate backwards through user, assistant, and doc context messages in this conversation
        var messages = conversation.Messages
            .Where(m => m.Author == AppConstants.ROLE_USER || 
                        m.Author == AppConstants.ROLE_ASSISTANT || 
                        (m.Author == AppConstants.ROLE_SYSTEM && m.Text.StartsWith("[System Context", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var m = messages[i];
            int tokens = _llamaService.CountTokens(m.Text);
            if (currentTotal + tokens > maxAllowedTokens)
            {
                // Too many tokens, truncate the rest of the older messages
                break;
            }
            history.Insert(0, (m.Author, m.Text));
            currentTotal += tokens;
        }

        return history;
    }

    public void UpdateContextUsage()
    {
        if (!_llamaService.IsModelLoaded)
        {
            ContextUsageString = $"0 / {ContextSize}";
            ContextUsageBrush = Brushes.Gray;
            return;
        }

        int count = 0;
        count += _llamaService.CountTokens(SystemPrompt);
        if (CurrentConversation != null)
        {
            foreach (var m in CurrentConversation.Messages)
            {
                if (m.Author == AppConstants.ROLE_USER || 
                    m.Author == AppConstants.ROLE_ASSISTANT || 
                    (m.Author == AppConstants.ROLE_SYSTEM && m.Text.StartsWith("[System Context", StringComparison.OrdinalIgnoreCase)))
                {
                    count += _llamaService.CountTokens(m.Text);
                }
            }
        }

        ContextUsageString = $"{count} / {ContextSize}";

        if (count > ContextSize * 0.9)
        {
            ContextUsageBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
        }
        else if (count > ContextSize * 0.7)
        {
            ContextUsageBrush = new SolidColorBrush(Color.FromRgb(253, 126, 20)); // Orange
        }
        else
        {
            ContextUsageBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
        }
    }

    private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "if", "then", "else", "when", "at", "by", "from", "for", "in", "out", "of", "on", "to", "with", "is", "was", "were", "are", "be", "been", "have", "has", "had", "this", "that", "these", "those"
    };

    private List<string> Tokenize(string text)
    {
        return text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.ToLowerInvariant().Trim())
                   .Where(t => t.Length > 2 && !StopWords.Contains(t))
                   .ToList();
    }

    private class ParagraphInfo
    {
        public string SourceFileName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public Dictionary<string, int> TermFrequencies { get; set; } = new Dictionary<string, int>();
        public int TotalTermCount { get; set; }
        public double Score { get; set; }
    }

    private List<string> ChunkText(string text, int chunkSizeWords = 150, int overlapWords = 20)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(text)) return chunks;

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return chunks;

        int step = chunkSizeWords - overlapWords;
        if (step <= 0) step = chunkSizeWords;

        for (int i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(chunkSizeWords).ToArray();
            if (chunkWords.Length == 0) break;
            chunks.Add(string.Join(" ", chunkWords));
            
            if (i + chunkSizeWords >= words.Length) break;
        }
        return chunks;
    }

    private string GetDocumentText(AttachedDocument doc)
    {
        if (!string.IsNullOrEmpty(doc.ExtractedText))
        {
            return doc.ExtractedText;
        }

        try
        {
            string filePath = Path.Combine("Data", "Documents", $"{doc.Id}.txt");
            if (File.Exists(filePath))
            {
                doc.ExtractedText = File.ReadAllText(filePath);
                return doc.ExtractedText;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load offloaded document text: {ex.Message}");
        }

        return string.Empty;
    }

    private bool IsQueryAboutDocument(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return false;
        var lowerPrompt = prompt.ToLowerInvariant();
        var keywords = new[] { "document", "pdf", "file", "attach", "summar", "about", "read", "overview", "contents", "explain", "what is", "what does", "tell me" };
        return keywords.Any(k => lowerPrompt.Contains(k));
    }

    private string GetFirstDocumentContentFallback(int maxCharacters)
    {
        var firstDoc = CurrentConversation?.AttachedDocuments.FirstOrDefault();
        if (firstDoc != null)
        {
            string fullText = GetDocumentText(firstDoc);
            if (!string.IsNullOrEmpty(fullText))
            {
                return fullText.Length > maxCharacters ? fullText.Substring(0, maxCharacters) + "..." : fullText;
            }
        }
        return string.Empty;
    }

    private string GetRelevantContext(string prompt, int maxCharacters = 2000)
    {
        if (CurrentConversation == null || CurrentConversation.AttachedDocuments.Count == 0)
            return string.Empty;

        // 1. Gather all paragraphs (chunks) across all attached documents
        var paragraphInfos = new List<ParagraphInfo>();
        foreach (var doc in CurrentConversation.AttachedDocuments)
        {
            string fullText = GetDocumentText(doc);
            var chunks = ChunkText(fullText, 150, 20);
            foreach (var p in chunks)
            {
                var cleanP = p.Trim();
                if (string.IsNullOrWhiteSpace(cleanP)) continue;

                var terms = Tokenize(cleanP);
                if (terms.Count == 0) continue;

                var tf = new Dictionary<string, int>();
                foreach (var term in terms)
                {
                    tf[term] = tf.TryGetValue(term, out int val) ? val + 1 : 1;
                }

                paragraphInfos.Add(new ParagraphInfo
                {
                    SourceFileName = doc.FileName,
                    Text = cleanP,
                    TermFrequencies = tf,
                    TotalTermCount = terms.Count
                });
            }
        }

        int N = paragraphInfos.Count;
        if (N == 0) return string.Empty;

        // 2. Compute Document Frequencies (DF)
        var df = new Dictionary<string, int>();
        foreach (var pi in paragraphInfos)
        {
            foreach (var term in pi.TermFrequencies.Keys)
            {
                df[term] = df.TryGetValue(term, out int val) ? val + 1 : 1;
            }
        }

        // 3. Compute Inverse Document Frequencies (IDF)
        var idf = new Dictionary<string, double>();
        foreach (var kvp in df)
        {
            // Smooth IDF: ln(1 + N / (1 + DF))
            idf[kvp.Key] = Math.Log(1.0 + (double)N / (1.0 + kvp.Value));
        }

        // 4. Tokenize the user prompt
        var queryTerms = Tokenize(prompt);
        if (queryTerms.Count == 0)
        {
            return IsQueryAboutDocument(prompt) ? GetFirstDocumentContentFallback(maxCharacters) : string.Empty;
        }

        var queryTf = new Dictionary<string, int>();
        foreach (var qTerm in queryTerms)
        {
            queryTf[qTerm] = queryTf.TryGetValue(qTerm, out int val) ? val + 1 : 1;
        }

        // Calculate query vector norm and score paragraphs
        double queryNormSquared = 0;
        foreach (var kvp in queryTf)
        {
            string term = kvp.Key;
            if (idf.TryGetValue(term, out double termIdf))
            {
                double weight = kvp.Value * termIdf;
                queryNormSquared += weight * weight;
            }
        }
        if (queryNormSquared == 0)
        {
            return IsQueryAboutDocument(prompt) ? GetFirstDocumentContentFallback(maxCharacters) : string.Empty;
        }
        double queryNorm = Math.Sqrt(queryNormSquared);

        // 5. Score paragraphs using Cosine Similarity
        foreach (var pi in paragraphInfos)
        {
            double dotProduct = 0;
            double docNormSquared = 0;

            // Calculate document vector norm (using all terms in the document)
            foreach (var kvp in pi.TermFrequencies)
            {
                string term = kvp.Key;
                double termIdf = idf.TryGetValue(term, out double val) ? val : 0.0;
                double weight = kvp.Value * termIdf;
                docNormSquared += weight * weight;

                if (queryTf.TryGetValue(term, out int qFreq))
                {
                    double qWeight = qFreq * termIdf;
                    dotProduct += weight * qWeight;
                }
            }

            if (docNormSquared > 0)
            {
                double docNorm = Math.Sqrt(docNormSquared);
                pi.Score = dotProduct / (queryNorm * docNorm);
            }
            else
            {
                pi.Score = 0;
            }
        }

        // 6. Rank paragraphs by score and take top ones within maxCharacters limit
        var rankedParagraphs = paragraphInfos
            .Where(p => p.Score > 0.01) // Filter out negligible matches
            .OrderByDescending(p => p.Score)
            .ToList();

        if (rankedParagraphs.Count == 0)
        {
            return IsQueryAboutDocument(prompt) ? GetFirstDocumentContentFallback(maxCharacters) : string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var p in rankedParagraphs)
        {
            if (sb.Length + p.Text.Length > maxCharacters)
            {
                if (sb.Length == 0)
                {
                    string truncated = p.Text.Length > maxCharacters 
                        ? p.Text.Substring(0, maxCharacters) + "..." 
                        : p.Text;
                    sb.AppendLine($"--- Snippet from {p.SourceFileName} (Relevance: {p.Score:F2}) ---");
                    sb.AppendLine(truncated);
                    sb.AppendLine();
                }
                break;
            }

            sb.AppendLine($"--- Snippet from {p.SourceFileName} (Relevance: {p.Score:F2}) ---");
            sb.AppendLine(p.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task HandleUserPrompt()
    {
        var prompt = UserInputText;
        if (string.IsNullOrWhiteSpace(prompt)) return;

        var currentConv = CurrentConversation;
        if (currentConv == null) return;

        if (currentConv.Title == AppConstants.FIRST_CONVERSATION_TITLE || 
            currentConv.Title.StartsWith("Conversation ", StringComparison.OrdinalIgnoreCase))
        {
            string cleanPrompt = prompt.Trim().Replace('\r', ' ').Replace('\n', ' ');
            currentConv.Title = cleanPrompt.Length > 25 ? cleanPrompt.Substring(0, 22).Trim() + "..." : cleanPrompt;
        }

        UserInputText = string.Empty;
        IsGenerating = true;
        LoadingState = AppLoadingState.Generating;
        StatusMessage = "Retrieving context and generating...";

        // RAG: Find relevant context from attached documents
        var context = GetRelevantContext(prompt);
        if (!string.IsNullOrEmpty(context))
        {
            // We add a hidden system message with the context before the user's prompt
            var contextMessage = new ChatMessage
            {
                Author = AppConstants.ROLE_SYSTEM,
                Text = $"[System Context from Documents]\n{context}",
                AuthorBrush = Brushes.Gray,
                // We can mark this to not be shown if we want, but for now we'll just make it subtle
            };
            
            // If we want it truly hidden from the UI but sent to the model:
            // We'll add it to the collection, then RestoreSession, then remove it if needed.
            // But keeping it visible (at least subtly) helps the user see what the model is "looking at".
            ChatMessages.Add(contextMessage);
            
            // Since we changed the history (added a message), we must re-sync the session 
            // but we only want to do this if we are using the KV cache system.
            // For simplicity, we'll invalidate the cache for this turn.
            InvalidateCurrentCache();
            RestoreContextFromConversation(currentConv);
        }

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
        TPS = 0;

        try
        {
            await Task.Run(async () => 
            {
                 var pipeline = new LLama.Sampling.DefaultSamplingPipeline
                 {
                     Temperature = (float)Temperature,
                     TopP = (float)TopP,
                     RepeatPenalty = AppConstants.DEFAULT_REPEAT_PENALTY
                 };

                 var inferenceParams = new LLama.Common.InferenceParams
                 {
                     MaxTokens = MaxTokens,
                     AntiPrompts = AppConstants.ANTI_PROMPTS.ToList(),
                     SamplingPipeline = pipeline
                 };

                 await foreach (var (text, metrics) in _llamaService.GenerateResponseWithMetricsAsync(prompt, inferenceParams, _cts.Token))
                 {
                     Application.Current.Dispatcher.Invoke(() =>
                     {
                        if (firstToken)
                        {
                            assistantMessage.Text = "";
                            firstToken = false;
                        }
                        assistantMessage.Text += text;
                        assistantMessage.Metrics = metrics;
                        TPS = metrics.TokensPerSecond;
                     });
                 }

                 // After successful generation, save the state
                 try
                 {
                     string cacheDir = Path.Combine("Data", "Cache");
                     Directory.CreateDirectory(cacheDir);
                     string cachePath = Path.Combine(cacheDir, $"{currentConv.Id}.bin");
                     _llamaService.SaveState(cachePath);
                     
                     Application.Current.Dispatcher.Invoke(() => 
                     {
                         currentConv.LastUsedModelPath = _llamaService.ModelPath;
                         UpdateContextUsage();
                     });
                 }
                 catch (Exception ex)
                 {
                     System.Diagnostics.Debug.WriteLine($"Failed to save KV cache: {ex}");
                 }
            });
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
            _dialogService.ShowErrorDialog($"An error occurred: {ex.Message}");
            assistantMessage.Text += string.Format(AppConstants.MESSAGE_GENERATION_ERROR, ex.Message);
            StatusMessage = "Generation error";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsGenerating = false;
            LoadingState = AppLoadingState.Idle;
            UpdateContextUsage();
        }
    }

    #endregion

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _llamaService.Dispose();
        SaveConversations();
        GC.SuppressFinalize(this);
    }
}
