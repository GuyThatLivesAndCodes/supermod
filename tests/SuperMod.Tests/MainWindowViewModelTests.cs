using Microsoft.Extensions.Logging;
using SuperMod.App.ViewModels;
using SuperMod.Configuration;
using SuperMod.Discord;
using Xunit;

namespace SuperMod.Tests;

public class MainWindowViewModelTests
{
    // Run posted UI callbacks inline so tests don't need an Avalonia dispatcher.
    private static MainWindowViewModel Build(
        FakeBotController controller, FakeConfigStore store)
        => new(controller, store, action => action());

    private static SuperModOptions SampleOptions() => new()
    {
        DiscordToken = "token-123",
        Rules = "No spam.",
        Ai = new AiOptions { Provider = "xai", BaseUrl = "http://x/v1", ApiKey = "key", Model = "grok-2-latest", Temperature = 0.5, RequestTimeoutSeconds = 90 },
        Moderation = new ModerationOptions { MessagesPerBatch = 7, ContextWindow = 14, MaxTimeoutMinutes = 600, DryRun = true, ProtectModerators = false, NotifyUsers = false }
    };

    [Fact]
    public void Loads_configuration_into_fields_on_construction()
    {
        var vm = Build(new FakeBotController(), new FakeConfigStore(SampleOptions()));

        Assert.Equal("token-123", vm.DiscordToken);
        Assert.Equal("No spam.", vm.Rules);
        Assert.Equal("xai", vm.SelectedProvider);
        Assert.Equal("http://x/v1", vm.BaseUrl);
        Assert.Equal("key", vm.ApiKey);
        Assert.Equal("grok-2-latest", vm.Model);
        Assert.Equal(0.5, vm.Temperature);
        Assert.Equal(90, vm.RequestTimeoutSeconds);
        Assert.Equal(7, vm.MessagesPerBatch);
        Assert.Equal(14, vm.ContextWindow);
        Assert.Equal(600, vm.MaxTimeoutMinutes);
        Assert.True(vm.DryRun);
        Assert.False(vm.ProtectModerators);
        Assert.False(vm.NotifyUsers);
    }

    [Fact]
    public void BuildOptions_round_trips_field_values()
    {
        var vm = Build(new FakeBotController(), new FakeConfigStore(SampleOptions()));

        var options = vm.BuildOptions();

        Assert.Equal("token-123", options.DiscordToken);
        Assert.Equal("xai", options.Ai.Provider);
        Assert.Equal("grok-2-latest", options.Ai.Model);
        Assert.Equal(0.5, options.Ai.Temperature);
        Assert.Equal(7, options.Moderation.MessagesPerBatch);
        Assert.Equal(14, options.Moderation.ContextWindow);
        Assert.True(options.Moderation.DryRun);
    }

    [Fact]
    public void Blank_optional_fields_become_null()
    {
        var vm = Build(new FakeBotController(), new FakeConfigStore());
        vm.BaseUrl = "   ";
        vm.ApiKey = "";

        var options = vm.BuildOptions();

        Assert.Null(options.Ai.BaseUrl);
        Assert.Null(options.Ai.ApiKey);
    }

    [Fact]
    public void Save_command_persists_options()
    {
        var store = new FakeConfigStore();
        var vm = Build(new FakeBotController(), store);
        vm.DiscordToken = "abc";

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal("abc", store.Saved!.DiscordToken);
    }

    [Fact]
    public async Task Start_with_invalid_config_faults_without_starting_the_bot()
    {
        var controller = new FakeBotController();
        var store = new FakeConfigStore();
        var vm = Build(controller, store);
        vm.DiscordToken = ""; // invalid

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(0, controller.StartCount);
        Assert.Equal(BotStatus.Faulted, vm.Status);
        Assert.NotEmpty(vm.Logs);
    }

    [Fact]
    public async Task Start_with_valid_config_starts_bot_and_saves()
    {
        var controller = new FakeBotController();
        var store = new FakeConfigStore(SampleOptions());
        var vm = Build(controller, store);

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(1, controller.StartCount);
        Assert.Equal("token-123", controller.StartedWith!.DiscordToken);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Stop_command_stops_the_bot()
    {
        var controller = new FakeBotController();
        var vm = Build(controller, new FakeConfigStore());
        controller.RaiseStatus(BotStatus.Running); // drives vm.Status -> Running

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(1, controller.StopCount);
    }

    [Fact]
    public void Status_change_event_updates_view_model_and_commands()
    {
        var controller = new FakeBotController();
        var vm = Build(controller, new FakeConfigStore());

        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));

        controller.RaiseStatus(BotStatus.Running);

        Assert.Equal(BotStatus.Running, vm.Status);
        Assert.Equal("Running", vm.StatusText);
        Assert.True(vm.IsRunning);
        Assert.False(vm.CanEditConfig);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public void Activity_event_is_prepended_to_feed()
    {
        var controller = new FakeBotController();
        var vm = Build(controller, new FakeConfigStore());

        controller.RaiseActivity(new ModerationActivity(DateTimeOffset.Now, "general", "deleted 2 messages"));
        controller.RaiseActivity(new ModerationActivity(DateTimeOffset.Now, "off-topic", "timed out alice for 30m"));

        Assert.Equal(2, vm.Activities.Count);
        Assert.Equal("#off-topic", vm.Activities[0].Channel); // newest first
        Assert.Contains("timed out", vm.Activities[0].Detail);
    }

    [Fact]
    public void Log_event_is_added_to_log_panel()
    {
        var controller = new FakeBotController();
        var vm = Build(controller, new FakeConfigStore());

        controller.RaiseLog(LogLevel.Warning, "something happened");

        var entry = Assert.Single(vm.Logs);
        Assert.Equal("Warning", entry.Level);
        Assert.Equal("something happened", entry.Message);
    }

    [Fact]
    public void BaseUrlWatermark_follows_selected_provider()
    {
        var vm = Build(new FakeBotController(), new FakeConfigStore());

        vm.SelectedProvider = "ollama";
        Assert.Equal("http://localhost:11434/v1", vm.BaseUrlWatermark);

        vm.SelectedProvider = "lmstudio";
        Assert.Equal("http://localhost:1234/v1", vm.BaseUrlWatermark);
    }
}
