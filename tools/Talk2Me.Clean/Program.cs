using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;
using Talk2Me.Core.Text;
using Talk2Me.Llm;
using Talk2Me.Windows.Security;

if (args.Length == 0)
{
    Console.WriteLine("usage: Talk2Me.Clean \"<transcript>\" [style=Natural] [model=claude-opus-5] [timeoutMs=8000]");
    Console.WriteLine();
    Console.WriteLine("Uses the key saved in Settings, or ANTHROPIC_API_KEY. Reads nothing else from settings.json,");
    Console.WriteLine("so it exercises the rewrite whether or not the app has it switched on.");
    return 1;
}

var transcript = args[0];
var style = args.Length > 1 ? Enum.Parse<CleanupStyle>(args[1], ignoreCase: true) : CleanupStyle.Natural;
var model = args.Length > 2 ? args[2] : new CleanupSettings().Model;

// A longer default than the app's: here we want to see the real latency, not a fallback.
var timeoutMs = args.Length > 3 ? int.Parse(args[3]) : 8000;

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; })
    .SetMinimumLevel(LogLevel.Debug));

var settings = new StaticSettings(new Talk2MeSettings
{
    Cleanup = new CleanupSettings
    {
        UseLlm = true,
        Style = style,
        Model = model,
        TimeoutMs = timeoutMs,
    },
});

var keys = new DpapiApiKeyStore(loggerFactory.CreateLogger<DpapiApiKeyStore>());
var llm = new ClaudeLlmClient(keys, loggerFactory.CreateLogger<ClaudeLlmClient>());

if (!llm.IsConfigured)
{
    Console.Error.WriteLine("No API key. Save one in Talk2Me's settings, or set ANTHROPIC_API_KEY.");
    return 2;
}

var cleaner = new LlmTextCleaner(llm, settings, loggerFactory.CreateLogger<LlmTextCleaner>());

Console.WriteLine();
Console.WriteLine($"Model {model}, style {style}, timeout {timeoutMs} ms");
Console.WriteLine();
Console.WriteLine("raw    : " + transcript);
Console.WriteLine("regex  : " + BasicTextCleaner.Clean(transcript, removeFillers: true));

var started = Stopwatch.GetTimestamp();
var cleaned = await cleaner.CleanAsync(transcript);
var elapsed = Stopwatch.GetElapsedTime(started);

Console.WriteLine("cleaned: " + cleaned);
Console.WriteLine();
Console.WriteLine($"{(int)elapsed.TotalMilliseconds} ms");
return 0;

internal sealed class StaticSettings(Talk2MeSettings settings) : ISettingsProvider
{
    public Talk2MeSettings Current { get; } = settings;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
