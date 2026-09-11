using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TawkType.Windows.Input;

// Reports what the focus probe sees, once a second, so you can click between windows and watch the
// verdict change. This is the part of the "can I type here?" check that cannot be unit tested: it
// depends on what each application chooses to expose through UI Automation.

var seconds = args.Length > 0 ? int.Parse(args[0]) : 10;

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Debug));

var probe = new UiaFocusProbe(loggerFactory.CreateLogger<UiaFocusProbe>());

Console.WriteLine($"Probing once a second for {seconds}s. Click into different windows.");
Console.WriteLine();
Console.WriteLine($"{"verdict",-12} {"types?",-7} {"ms",-6} target");
Console.WriteLine(new string('-', 78));

for (var i = 0; i < seconds; i++)
{
    var started = Stopwatch.GetTimestamp();
    var target = probe.Probe();
    var elapsed = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    Console.WriteLine($"{target.Verdict,-12} {target.CanType,-7} {elapsed,-6} {target.Description}");

    // The caret read is the other half, and the half most likely to differ between applications: a
    // control can be perfectly typeable and still expose nothing about where the caret is in it.
    var caretStarted = Stopwatch.GetTimestamp();
    var caret = probe.ReadCaret();
    var caretMs = (int)Stopwatch.GetElapsedTime(caretStarted).TotalMilliseconds;

    Console.WriteLine(caret.IsUnknown
        ? $"{"caret",-12} {"-",-7} {caretMs,-6} (nothing readable)"
        : $"{"caret",-12} {(caret.HasSelection ? "sel" : "-"),-7} {caretMs,-6} "
            + $"before=[{Show(caret.Before)}] after=[{Show(caret.After)}]");

    await Task.Delay(1000);
}

// Line breaks would wreck the table, and the whole point is to see the characters either side.
static string Show(string? text) => text?.Replace("\r", "\\r").Replace("\n", "\\n") ?? string.Empty;
