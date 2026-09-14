using System.Globalization;
using MooTrack.Derivation;

namespace MooTrack.Cli;

public sealed record Arguments
{
    public string? SleepStudy { get; init; }
    public string? Ndjson { get; init; }
    public string Output { get; init; } = ".";
    public TimeSpan Offset { get; init; } = TimeSpan.Zero;
    public DerivationOptions Options { get; init; } = new();

    public static Arguments Parse(string[] args)
    {
        var parsed = new Arguments();
        var options = parsed.Options;

        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;

            switch (args[i])
            {
                case "--sleepstudy":
                    parsed = parsed with { SleepStudy = Require(args[i], value) };
                    i++;
                    break;
                case "--ndjson":
                    parsed = parsed with { Ndjson = Require(args[i], value) };
                    i++;
                    break;
                case "--out":
                    parsed = parsed with { Output = Require(args[i], value) };
                    i++;
                    break;
                case "--offset":
                    parsed = parsed with { Offset = ParseOffset(Require(args[i], value)) };
                    i++;
                    break;
                case "--bridge":
                    options = options with { BridgeThreshold = Minutes(Require(args[i], value)) };
                    i++;
                    break;
                case "--confirm":
                    options = options with { ConfirmationWindow = Minutes(Require(args[i], value)) };
                    i++;
                    break;
                case "--fringe-max":
                    options = options with { FringeMaxDuration = Minutes(Require(args[i], value)) };
                    i++;
                    break;
                case "--fringe-gap":
                    options = options with { FringeGap = Minutes(Require(args[i], value)) };
                    i++;
                    break;
                case "--gap-tolerance":
                    options = options with { GapTolerance = Minutes(Require(args[i], value)) };
                    i++;
                    break;
                case "--bridge-across-lock":
                    options = options with { BridgeAcrossLock = true };
                    break;
                default:
                    throw new ArgumentException($"unknown argument: {args[i]}");
            }
        }

        if (parsed.SleepStudy is null == (parsed.Ndjson is null))
            throw new ArgumentException("supply exactly one of --sleepstudy or --ndjson");

        return parsed with { Options = options };
    }

    private static string Require(string name, string? value) =>
        value is null || value.StartsWith("--", StringComparison.Ordinal)
            ? throw new ArgumentException($"{name} needs a value")
            : value;

    private static TimeSpan Minutes(string value) =>
        TimeSpan.FromMinutes(Double.Parse(value, CultureInfo.InvariantCulture));

    private static TimeSpan ParseOffset(string value) =>
        value.StartsWith('-')
            ? -TimeSpan.Parse(value[1..], CultureInfo.InvariantCulture)
            : TimeSpan.Parse(value.TrimStart('+'), CultureInfo.InvariantCulture);
}
