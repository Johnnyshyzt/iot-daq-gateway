namespace Adapters.Fanuc.Focas;

/// <summary>
/// Maps FOCAS status / program numbers onto the same <c>state</c> / <c>alarm</c> / <c>program</c>
/// points emitted by <see cref="Fake.FakeFanucAdapter"/>.
/// </summary>
internal static class FocasPointMapper
{
    // Series 16i/18i/21i/0i/30i cnc_statinfo: run == 3 is automatic START.
    private const short RunStart = 3;
    private const short AlarmRaised = 1;

    public static string MapState(in FocasStatInfo status)
    {
        if (status.Emergency != 0 || status.Alarm == AlarmRaised)
        {
            return "ALARM";
        }

        if (status.Run == RunStart)
        {
            return "RUNNING";
        }

        return "IDLE";
    }

    public static string QualityFor(string state) =>
        string.Equals(state, "ALARM", StringComparison.Ordinal) ? "uncertain" : "good";

    public static string FormatProgram(int programNumber)
    {
        if (programNumber < 0)
        {
            programNumber = 0;
        }

        return programNumber <= 9999 ? $"O{programNumber:D4}" : $"O{programNumber}";
    }
}
