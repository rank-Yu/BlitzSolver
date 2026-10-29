// R3a probe-only records. These are evidence for R3b, not a production policy protocol.
using System.Diagnostics;
using System.Text.Json;

namespace Differential;

internal sealed class RunProbeTelemetry(string directory)
{
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _milliseconds = [];
    private string _phase = "other";
    private long _last = Stopwatch.GetTimestamp();
    private int _sequence;

    private readonly List<(object Id, string Phase)> _active = [];
    internal string ActivePhase => _phase;

    internal IDisposable Measure(string phase)
    {
        object id = new();
        _active.Add((id, phase));
        Change(phase);
        return new Scope(() =>
        {
            int index = _active.FindIndex(entry => ReferenceEquals(entry.Id, id));
            if (index < 0) throw new InvalidOperationException("计时scope重复释放");
            _active.RemoveAt(index);
            Change(_active.Count == 0 ? "other" : _active[^1].Phase);
        });
    }

    private void Change(string phase)
    {
        long now = Stopwatch.GetTimestamp();
        _milliseconds[_phase] = _milliseconds.GetValueOrDefault(_phase)
            + Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        _phase = phase;
    }

    internal void Write(string kind, object payload)
    {
        using (Measure("logging"))
            File.AppendAllText(Path.Combine(directory, "trace.jsonl"),
                JsonSerializer.Serialize(new { sequence = _sequence++, kind, payload }) + "\n");
    }

    internal void Progress(string phase, object facts)
    {
        using (Measure("logging"))
            File.WriteAllText(Path.Combine(directory, "progress.json"),
                JsonSerializer.Serialize(new { phase, wallMs = _wall.Elapsed.TotalMilliseconds, facts }));
    }

    internal void Finish(string status, int exitCode, object facts)
    {
        Change("other");
        double total = _wall.Elapsed.TotalMilliseconds;
        double sum = _milliseconds.Values.Sum();
        using Process process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, status, exitCode, facts, totalMs = total,
            segmentsMs = _milliseconds, segmentSumMs = sum, unclassifiedMs = total - sum,
            peakWorkingSetBytes = process.PeakWorkingSet64,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
