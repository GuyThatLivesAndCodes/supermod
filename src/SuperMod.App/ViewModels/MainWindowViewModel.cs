using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SuperMod.App.Services;
using SuperMod.Configuration;
using SuperMod.Discord;

namespace SuperMod.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private readonly IBotController _controller;
    private readonly IConfigStore _configStore;
    private readonly Action<Action> _post;

    public ObservableCollection<ActivityItem> Activities { get; } = new();
    public ObservableCollection<LogItem> Logs { get; } = new();
    public string[] Providers { get; } = { "ollama", "lmstudio", "xai", "openai" };

    [ObservableProperty] private string _discordToken = "";
    [ObservableProperty] private string _rules = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseUrlWatermark))]
    private string _selectedProvider = "ollama";

    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private double _temperature = 0.2;
    [ObservableProperty] private int _requestTimeoutSeconds = 120;
    [ObservableProperty] private int _messagesPerBatch = 10;
    [ObservableProperty] private int _contextWindow = 20;
    [ObservableProperty] private int _maxTimeoutMinutes = 1440;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty] private bool _protectModerators = true;
    [ObservableProperty] private bool _notifyUsers = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsRunning), nameof(CanEditConfig))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private BotStatus _status = BotStatus.Stopped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusDetail))]
    private string _statusDetail = "";

    public bool HasStatusDetail => !string.IsNullOrEmpty(StatusDetail);

    public MainWindowViewModel(IBotController controller, IConfigStore configStore, Action<Action> post)
    {
        _controller = controller;
        _configStore = configStore;
        _post = post;

        _controller.StatusChanged += OnStatusChanged;
        _controller.ActivityRecorded += OnActivityRecorded;
        _controller.LogEmitted += OnLogEmitted;

        LoadFromOptions(_configStore.Load());
    }

    public string StatusText => Status switch
    {
        BotStatus.Stopped => "Stopped",
        BotStatus.Starting => "Starting…",
        BotStatus.Running => "Running",
        BotStatus.Stopping => "Stopping…",
        BotStatus.Faulted => "Error",
        _ => Status.ToString()
    };

    public bool IsRunning => Status == BotStatus.Running;
    public bool CanEditConfig => Status is BotStatus.Stopped or BotStatus.Faulted;

    public string BaseUrlWatermark
    {
        get
        {
            var resolved = new AiOptions { Provider = SelectedProvider }.ResolveBaseUrl();
            return string.IsNullOrEmpty(resolved) ? "https://your-endpoint/v1" : resolved;
        }
    }

    private bool CanStart() => Status is BotStatus.Stopped or BotStatus.Faulted;
    private bool CanStop() => Status is BotStatus.Running or BotStatus.Starting;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        var options = BuildOptions();

        try
        {
            options.Validate();
        }
        catch (Exception ex)
        {
            Status = BotStatus.Faulted;
            StatusDetail = ex.Message;
            AddLog(LogLevel.Error, ex.Message);
            return;
        }

        _configStore.Save(options);

        try
        {
            await _controller.StartAsync(options, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Status is already set to Faulted by the runner; surface the detail.
            StatusDetail = ex.Message;
            AddLog(LogLevel.Error, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task Stop() => await _controller.StopAsync();

    [RelayCommand]
    private void Save()
    {
        _configStore.Save(BuildOptions());
        AddLog(LogLevel.Information, $"Settings saved to {_configStore.Path}");
    }

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    public SuperModOptions BuildOptions() => new()
    {
        DiscordToken = DiscordToken?.Trim() ?? "",
        Rules = Rules ?? "",
        Ai = new AiOptions
        {
            Provider = SelectedProvider,
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
            Model = Model?.Trim() ?? "",
            Temperature = Temperature,
            RequestTimeoutSeconds = RequestTimeoutSeconds
        },
        Moderation = new ModerationOptions
        {
            MessagesPerBatch = MessagesPerBatch,
            ContextWindow = ContextWindow,
            MaxTimeoutMinutes = MaxTimeoutMinutes,
            DryRun = DryRun,
            ProtectModerators = ProtectModerators,
            NotifyUsers = NotifyUsers
        }
    };

    private void LoadFromOptions(SuperModOptions options)
    {
        DiscordToken = options.DiscordToken;
        Rules = options.Rules;
        SelectedProvider = options.Ai.Provider;
        BaseUrl = options.Ai.BaseUrl ?? "";
        ApiKey = options.Ai.ApiKey ?? "";
        Model = options.Ai.Model;
        Temperature = options.Ai.Temperature;
        RequestTimeoutSeconds = options.Ai.RequestTimeoutSeconds;
        MessagesPerBatch = options.Moderation.MessagesPerBatch;
        ContextWindow = options.Moderation.ContextWindow;
        MaxTimeoutMinutes = options.Moderation.MaxTimeoutMinutes;
        DryRun = options.Moderation.DryRun;
        ProtectModerators = options.Moderation.ProtectModerators;
        NotifyUsers = options.Moderation.NotifyUsers;
    }

    private void OnStatusChanged(BotStatus status, string? detail) => _post(() =>
    {
        Status = status;
        if (!string.IsNullOrEmpty(detail))
            StatusDetail = detail!;
    });

    private void OnActivityRecorded(ModerationActivity activity) => _post(() =>
        Activities.Insert(0, new ActivityItem(
            activity.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            "#" + activity.Channel,
            activity.Detail)));

    private void OnLogEmitted(LogLevel level, string message) => _post(() => AddLog(level, message));

    private void AddLog(LogLevel level, string message)
    {
        Logs.Insert(0, new LogItem(DateTime.Now.ToString("HH:mm:ss"), level.ToString(), message));
        while (Logs.Count > MaxLogLines)
            Logs.RemoveAt(Logs.Count - 1);
    }
}
