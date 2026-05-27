namespace SuperMod.App.ViewModels;

/// <summary>A row in the live moderation-activity feed.</summary>
public sealed record ActivityItem(string Time, string Channel, string Detail);

/// <summary>A row in the log panel.</summary>
public sealed record LogItem(string Time, string Level, string Message);
