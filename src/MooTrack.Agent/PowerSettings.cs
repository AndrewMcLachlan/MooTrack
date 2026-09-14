namespace MooTrack.Agent;

/// <summary>
/// Chooses the power setting GUIDs for the session the process is actually in.
/// The session-scoped and session-0 settings are not interchangeable: registering
/// the wrong one is rejected with ERROR_INVALID_PARAMETER, which surfaces as a
/// service that starts cleanly and then never observes anything.
/// </summary>
public static class PowerSettings
{
    public static readonly Guid GlobalUserPresence = new("786e8a1d-b427-4344-9207-09e70bdcbea9");

    public static readonly Guid SessionUserPresence = new("3c0f4548-c03f-4c4d-b9f2-237ede686376");

    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    public static Guid UserPresence(uint sessionId) =>
        sessionId == 0 ? GlobalUserPresence : SessionUserPresence;

    public static Guid DisplayState(uint sessionId) => ConsoleDisplayState;

    public static string Describe(Guid setting) =>
        setting == GlobalUserPresence ? "GUID_GLOBAL_USER_PRESENCE"
        : setting == SessionUserPresence ? "GUID_SESSION_USER_PRESENCE"
        : setting == ConsoleDisplayState ? "GUID_CONSOLE_DISPLAY_STATE"
        : setting.ToString();
}
