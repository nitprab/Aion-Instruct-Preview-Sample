using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using AionInstructPreview.Chat.Models;
using global::AionInstructPreview.Text;
using global::AionInstructPreview.Imaging;
using Windows.Storage;

namespace AionInstructPreview.Chat.ViewModels;

// Owns: the conversation transcript (ObservableCollection<Message>),
// the Aion Instruct Preview model client (loaded asynchronously in the ctor), the model
// state machine, and the Send + NewConversation commands.
//
// Threading: captures the UI-thread DispatcherQueue at construction time;
// every UI-bound state change funnels through TryEnqueue. The Aion Instruct Preview
// Progress callbacks fire on a background thread and are marshalled back to the UI thread.
public sealed class ChatViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private AionInstructClient? _aionClient;
    private AionImageDescriptionClient? _descriptionClient;

    private ModelState _state = ModelState.Loading;
    private string _promptText = string.Empty;
    private string? _errorMessage;
    private bool _isContextFull;
    private bool _disposed;
    private string _systemPrompt = string.Empty;

    // The system prompt baked into the live LanguageModelContext. A context is immutable
    // once created, so a changed prompt only takes effect on the next CreateContext.
    private string _appliedSystemPrompt = string.Empty;
    private ImageDescriptionKind _selectedImageKind = ImageDescriptionKind.DetailedDescription;

    public ChatViewModel()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "ChatViewModel must be constructed on a UI thread that has a DispatcherQueue.");

        Messages = new ObservableCollection<Message>();
        ImageMessages = new ObservableCollection<Message>();
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend);
        NewConversationCommand = new RelayCommand(
            _ => StartNewConversation(),
            _ => CanStartNewConversation);

        _ = LoadModelAsync();
    }

    public ObservableCollection<Message> Messages { get; }

    // Image description keeps its own transcript. The two features are independent:
    // describing an image neither reads nor extends the chat conversation context.
    public ObservableCollection<Message> ImageMessages { get; }

    // Applied when a new conversation is started, via CreateContext(String systemPrompt).
    // Empty means "use the stack's default assistant prompt".
    public string SystemPrompt
    {
        get => _systemPrompt;
        set
        {
            if (_systemPrompt == value) return;
            _systemPrompt = value;
            Raise();
            Raise(nameof(SystemPromptPendingVisibility));
        }
    }

    // Shown when the typed system prompt differs from the one the live context was
    // built with AND there is history worth preserving. With an empty transcript the
    // prompt is applied silently on the next send, so no nag is needed.
    public Visibility SystemPromptPendingVisibility =>
        (_systemPrompt != _appliedSystemPrompt && Messages.Count > 0)
            ? Visibility.Visible : Visibility.Collapsed;

    public ICommand SendCommand { get; }
    public ICommand NewConversationCommand { get; }

    // The four description styles the SDK supports, in enum order. Bound to the
    // ComboBox next to the attach-image button.
    public ImageDescriptionKind[] ImageKinds { get; } =
    {
        ImageDescriptionKind.BriefDescription,
        ImageDescriptionKind.DetailedDescription,
        ImageDescriptionKind.DiagramDescription,
        ImageDescriptionKind.AccessibleDescription,
    };

    public ImageDescriptionKind SelectedImageKind
    {
        get => _selectedImageKind;
        set
        {
            if (_selectedImageKind == value) return;
            _selectedImageKind = value;
            Raise();
        }
    }

    // The vision models load lazily on first use, so the attach button stays
    // enabled whenever the model is idle. Describing sets State to Generating,
    // which also disables the chat input -- both features share one model.
    public bool DescribeEnabled => _state == ModelState.Ready;

    public string PromptText
    {
        get => _promptText;
        set
        {
            if (_promptText == value) return;
            _promptText = value;
            Raise();
            // SendEnabled depends on PromptText.
            Raise(nameof(SendEnabled));
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ModelState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            Raise();
            Raise(nameof(LoadingVisibility));
            Raise(nameof(ErrorVisibility));
            Raise(nameof(ChatVisibility));
            Raise(nameof(InputEnabled));
            Raise(nameof(SendEnabled));
            Raise(nameof(CanStartNewConversation));
            Raise(nameof(NewConversationButtonEnabled));
            Raise(nameof(DescribeEnabled));
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NewConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            Raise();
        }
    }

    // True when the latest GenerateResponseAsync returned
    // PromptLargerThanContext. Drives the "context full" banner + the
    // primary positioning of the New conversation affordance.
    public bool IsContextFull
    {
        get => _isContextFull;
        private set
        {
            if (_isContextFull == value) return;
            _isContextFull = value;
            Raise();
            Raise(nameof(ContextFullBannerVisibility));
            Raise(nameof(InputEnabled));
            Raise(nameof(SendEnabled));
            (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    // UI-facing bindings. Visibility-typed properties avoid the need for a
    // BoolToVisibilityConverter in XAML.
    public Visibility LoadingVisibility =>
        _state == ModelState.Loading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility =>
        _state == ModelState.Error ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChatVisibility =>
        (_state == ModelState.Ready || _state == ModelState.Generating)
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ContextFullBannerVisibility =>
        _isContextFull ? Visibility.Visible : Visibility.Collapsed;

    // Input + Send are disabled when the conversation is full — the user
    // must start a new conversation before sending another prompt. The
    // banner explains why.
    public bool InputEnabled => _state == ModelState.Ready && !_isContextFull;
    public bool SendEnabled => InputEnabled && !string.IsNullOrWhiteSpace(_promptText);
    public bool CanSend => SendEnabled;

    // New conversation is available whenever the model is loaded, except during generation.
    public bool CanStartNewConversation =>
        _aionClient is not null && _state != ModelState.Loading && _state != ModelState.Generating;
    public bool NewConversationButtonEnabled => CanStartNewConversation;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task LoadModelAsync()
    {
        try
        {
            _aionClient = await AionInstructClient.CreateAsync().ConfigureAwait(true);
            State = ModelState.Ready;
            Raise(nameof(CanStartNewConversation));
            Raise(nameof(NewConversationButtonEnabled));
            (NewConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage =
                $"Aion Instruct Preview couldn't load.{Environment.NewLine}{ex.Message}{Environment.NewLine}" +
                "Make sure the Aion Instruct Preview framework MSIX is installed -- run Bootstrap.ps1, or scripts\\Diagnose-AionInstructPreview.ps1 to check every prerequisite.";
            State = ModelState.Error;
        }
    }

    public async Task SendAsync()
    {
        if (!CanSend) return;
        if (_aionClient == null) return;

        var prompt = _promptText.Trim();
        if (prompt.Length == 0) return;

        // Snapshot + clear before the await so the input box empties immediately.
        PromptText = string.Empty;

        // A context is immutable, so a changed system prompt needs a new one. With an
        // empty transcript there is no history to lose, so apply it silently rather than
        // making the user discover the "New conversation" step. With history, the banner
        // (SystemPromptPendingVisibility) tells them to start a new conversation instead.
        if (_systemPrompt != _appliedSystemPrompt && Messages.Count == 0)
        {
            _aionClient.StartNewConversation(_systemPrompt);
            _appliedSystemPrompt = _systemPrompt;
            Raise(nameof(SystemPromptPendingVisibility));
        }

        Messages.Add(new Message(MessageRole.User, prompt, MessageStatus.Complete));
        var aionMessage = new Message(MessageRole.Aion, string.Empty, MessageStatus.Streaming);
        Messages.Add(aionMessage);

        State = ModelState.Generating;

        try
        {
            var generation = await _aionClient.GenerateAsync(
                prompt,
                onToken: token =>
                {
                    // Progress fires on a background thread; marshal to UI.
                    _dispatcher.TryEnqueue(() => aionMessage.Text += token);
                }).ConfigureAwait(true);

            var result = generation.Response;
            aionMessage.Metrics = generation.Metrics;

            switch (result.Status)
            {
                case LanguageModelResponseStatus.Complete:
                    // Use the final accumulated text if it differs from the streamed text.
                    if (aionMessage.Text != result.Text)
                    {
                        aionMessage.Text = result.Text;
                    }
                    aionMessage.Status = MessageStatus.Complete;
                    break;

                case LanguageModelResponseStatus.PromptLargerThanContext:
                    // The conversation has filled Aion Instruct Preview's context window.
                    // Surface this as its own UX, distinct from Error — the
                    // user can recover with "New conversation". Any partial
                    // text Aion Instruct Preview produced before overflow is preserved.
                    aionMessage.StatusDetail =
                        "This conversation reached Aion Instruct Preview's context limit. " +
                        "Start a new conversation to keep chatting.";
                    aionMessage.Status = MessageStatus.Error;
                    IsContextFull = true;
                    break;

                default:
                    aionMessage.StatusDetail = $"Aion Instruct Preview returned Status={result.Status}.";
                    aionMessage.Status = MessageStatus.Error;
                    break;
            }
        }
        catch (Exception ex)
        {
            aionMessage.StatusDetail = ex.Message;
            aionMessage.Status = MessageStatus.Error;
        }
        finally
        {
            State = ModelState.Ready;
        }
    }

    // Describes a user-picked image and appends both the thumbnail entry and the
    // generated description to the transcript.
    //
    // The image description pipeline is independent of the chat context: it does not
    // read or extend the conversation history, and it mirrors the inbox
    // ImageDescriptionGenerator API exactly.
    public async Task DescribeImageAsync(StorageFile file)
    {
        if (file is null) return;
        if (!DescribeEnabled) return;

        // Set before the first await so a second click can't slip through.
        State = ModelState.Generating;

        var kind = _selectedImageKind;

        var userMessage = new Message(
            MessageRole.User,
            $"Describe this image ({KindLabel(kind)})",
            MessageStatus.Complete);

        try
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using (var thumbStream = await file.OpenReadAsync())
            {
                await bitmap.SetSourceAsync(thumbStream);
            }
            userMessage.Image = bitmap;
        }
        catch (Exception)
        {
            // A thumbnail is a nicety; a decode failure here must not block description.
        }

        ImageMessages.Add(userMessage);

        var aionMessage = new Message(MessageRole.Aion, string.Empty, MessageStatus.Streaming);
        ImageMessages.Add(aionMessage);

        try
        {
            if (_descriptionClient is null)
            {
                aionMessage.StatusDetail =
                    "Loading the vision models (SigLIP2 + projector). This takes ~20 seconds the first time.";
                _descriptionClient = await AionImageDescriptionClient.CreateAsync().ConfigureAwait(true);
                aionMessage.StatusDetail = null;
            }

            var generation = await _descriptionClient.DescribeAsync(
                file,
                kind,
                onToken: delta =>
                {
                    // Progress reports per-token deltas, exactly like the chat path
                    // (DescriptionSink::OnProcessingUpdate forwards only the new text).
                    // Append, don't assign.
                    _dispatcher.TryEnqueue(() => aionMessage.Text += delta);
                }).ConfigureAwait(true);

            var result = generation.Response;
            aionMessage.Metrics = generation.Metrics;

            if (result.Status == ImageDescriptionResultStatus.Complete)
            {
                if (aionMessage.Text != result.Description)
                {
                    aionMessage.Text = result.Description;
                }
                aionMessage.Status = MessageStatus.Complete;
            }
            else
            {
                aionMessage.StatusDetail = $"Image description returned Status={result.Status}.";
                aionMessage.Status = MessageStatus.Error;
            }
        }
        catch (Exception ex)
        {
            aionMessage.StatusDetail = ex.Message;
            aionMessage.Status = MessageStatus.Error;
        }
        finally
        {
            State = ModelState.Ready;
        }
    }

    private static string KindLabel(ImageDescriptionKind kind) => kind switch
    {
        ImageDescriptionKind.BriefDescription => "brief",
        ImageDescriptionKind.DetailedDescription => "detailed",
        ImageDescriptionKind.DiagramDescription => "diagram",
        ImageDescriptionKind.AccessibleDescription => "accessible",
        _ => kind.ToString(),
    };

    // Discard the in-process LanguageModelContext (and its accumulated
    // conversation history) and open a fresh one. Clears the transcript
    // too so the UX matches the model state.
    public void StartNewConversation()
    {
        if (!CanStartNewConversation) return;
        if (_aionClient == null) return;

        _aionClient.StartNewConversation(_systemPrompt);
        _appliedSystemPrompt = _systemPrompt;
        Messages.Clear();
        IsContextFull = false;
        Raise(nameof(SystemPromptPendingVisibility));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _aionClient?.Dispose();
        _aionClient = null;
        _descriptionClient?.Dispose();
        _descriptionClient = null;
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Minimal ICommand impl so we don't pull in CommunityToolkit.Mvvm.
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
