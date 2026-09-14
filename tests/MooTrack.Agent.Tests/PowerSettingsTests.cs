using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class PowerSettingsTests
{
    private static readonly Guid GlobalUserPresence = new("786e8a1d-b427-4344-9207-09e70bdcbea9");
    private static readonly Guid SessionUserPresence = new("3c0f4548-c03f-4c4d-b9f2-237ede686376");
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [Fact]
    public void UserPresence_InSessionZero_UsesTheGlobalSetting()
    {
        Assert.Equal(GlobalUserPresence, PowerSettings.UserPresence(sessionId: 0));
    }

    [Fact]
    public void UserPresence_InAnInteractiveSession_UsesTheSessionSetting()
    {
        Assert.Equal(SessionUserPresence, PowerSettings.UserPresence(sessionId: 1));
    }

    [Fact]
    public void DisplayState_IsTheConsoleSetting_WhicheverSession()
    {
        Assert.Equal(ConsoleDisplayState, PowerSettings.DisplayState(sessionId: 0));
        Assert.Equal(ConsoleDisplayState, PowerSettings.DisplayState(sessionId: 2));
    }

    [Fact]
    public void Describe_NamesTheSettingForLogging()
    {
        Assert.Equal("GUID_GLOBAL_USER_PRESENCE", PowerSettings.Describe(GlobalUserPresence));
        Assert.Equal("GUID_SESSION_USER_PRESENCE", PowerSettings.Describe(SessionUserPresence));
        Assert.Equal("GUID_CONSOLE_DISPLAY_STATE", PowerSettings.Describe(ConsoleDisplayState));
    }
}
