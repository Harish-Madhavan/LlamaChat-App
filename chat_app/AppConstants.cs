using System.Collections.Generic;

namespace LlamaChatApp;

public static class AppConstants
{
    // File Paths
    public const string SETTINGS_FILE_PATH = "Data/settings.json";
    public const string CONVERSATIONS_FILE_PATH = "Data/conversations.json";

    // Conversation
    public const string FIRST_CONVERSATION_TITLE = "New Chat";
    public const string DEFAULT_CONVERSATION_TITLE = "Conversation {0}";
    
    // Roles
    public const string ROLE_SYSTEM = "System";
    public const string ROLE_USER = "User";
    public const string ROLE_ASSISTANT = "Assistant";
    public const string ROLE_ERROR = "Error";

    // Messages
    public const string MESSAGE_WELCOME = "Welcome! Load a model to start chatting.";
    public const string MESSAGE_FILE_NOT_FOUND = "File not found.";
    public const string MESSAGE_LOADING = "Loading...";
    public const string MESSAGE_LOADED_SUCCESS = "Model {0} loaded successfully.";
    public const string MESSAGE_LOAD_FAILED = "Failed to load model: {0}";
    public const string MESSAGE_CHAT_CLEARED = "Chat cleared.";
    public const string MESSAGE_GENERATION_STOPPED = " [Generation stopped]";
    public const string MESSAGE_GENERATION_ERROR = " [Error: {0}]";

    // Defaults
    public const double DEFAULT_TEMPERATURE = 0.7;
    public const double DEFAULT_TOP_P = 0.9;
    public const int DEFAULT_MAX_TOKENS = 256;
    public const int DEFAULT_GPU_LAYERS = 0;
    public const int DEFAULT_CONTEXT_SIZE = 512;
    public const string DEFAULT_SYSTEM_PROMPT = "You are a helpful assistant.";
    public const float DEFAULT_REPEAT_PENALTY = 1.1f;

    public static readonly List<string> ANTI_PROMPTS = new List<string> { "User:", "\nUser" };

    // Colors
    public static class Colors
    {
        // User: DodgerBlue (R:30, G:144, B:255)
        public const byte USER_R = 30;
        public const byte USER_G = 144;
        public const byte USER_B = 255;

        // Assistant: Light Greenish
        public const byte ASSISTANT_R = 220;
        public const byte ASSISTANT_G = 248;
        public const byte ASSISTANT_B = 198;
    }
}
