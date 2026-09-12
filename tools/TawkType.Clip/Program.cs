using TawkType.Windows.Injection;

// What a pasted dictation does to the clipboard, minus the Ctrl+V — which is the only part that needs
// somebody's keyboard and somebody's window. Copy something first: an image, a file in Explorer,
// styled text from Word or a browser. Then run this and see whether it survives.
//
// The unit tests cover the decision (ClipboardRestore); this covers the half that is a fact about
// other people's software, namely which formats they put on the clipboard and whether those formats
// can be copied aside and put back at all.

// --read only looks. Everything else borrows the clipboard for real, which is worth knowing before
// running it over something you cannot copy again.
var readOnly = args.Length > 0 && args[0] == "--read";
var text = args.Length > 0 && !readOnly ? string.Join(' ', args) : "TawkType clipboard round trip";

Console.WriteLine("On the clipboard now:");
Console.WriteLine();

var before = ClipboardDiagnostics.Describe();
if (before.Count == 0)
{
    Console.WriteLine("  (nothing — copy something first to make this worth running)");
}

foreach (var format in before)
{
    Console.WriteLine($"  {(format.Copied ? "kept" : "LOST"),-5} {format.Name,-32} {Bytes(format)}");
}

if (readOnly)
{
    Console.WriteLine();
    Console.WriteLine("--read: nothing was written. Run it without --read to do the round trip.");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"Borrowing it to paste {text.Length} characters…");
Console.WriteLine();

var report = ClipboardDiagnostics.RoundTrip(text);

Console.WriteLine($"  write            {report.Write}");
Console.WriteLine($"  text arrived     {report.OurTextArrived}");
Console.WriteLine($"  everything kept  {report.SnapshotComplete}");
Console.WriteLine($"  decision         {report.Decision}");
Console.WriteLine($"  handed back      {report.HandedBack}");
Console.WriteLine();

if (report.Differences.Count == 0)
{
    Console.WriteLine(report.SnapshotComplete
        ? "The clipboard came back exactly as it was found."
        : "The clipboard came back, but something on it could not be copied aside — see LOST above.");
}
else
{
    Console.WriteLine("The clipboard did NOT come back as it was:");
    foreach (var difference in report.Differences)
    {
        Console.WriteLine($"  {difference}");
    }
}

Console.WriteLine();
Console.WriteLine("Paste somewhere now to confirm it by hand. Nothing here sent a keystroke.");

return report.Faithful ? 0 : 1;

static string Bytes(ClipboardFormatReport format) =>
    format.Copied ? $"{format.Bytes:n0} bytes" : "not memory-backed";
