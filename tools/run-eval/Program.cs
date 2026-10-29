using System.Text.Json;
using BlitzSolver.OfflineHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.WriteLine("run-eval --game-data PATH [--seed R3B2-DECISION] [--character Ironclad] [--ascension 0] [--unlocks neow-epoch-v1] [--tier Low|Medium|High|ExtraHigh] [--timeout-seconds 300] [--stop-after first-combat|next-room|rooms] [--max-rooms 4] [--max-decisions 256] [--max-steps 512]");
            Console.WriteLine("Starts one native run; default stops before rewards, next-room pauses after entering the next room, rooms completes a bounded number of Act1 map rooms. JSON output; Ctrl+C cancels.");
            return 0;
        }
        try
        {
            string? data = null;
            string stopAfter = "first-combat";
            string seed = "R3B2-DECISION", character = "Ironclad", unlocks = "neow-epoch-v1", tier = "Low";
            int ascension = 0, seconds = 300, maxRooms = 4, maxDecisions = 256, maxSteps = 512;
            HashSet<string> seen = [];
            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (!seen.Add(name) || i + 1 == args.Length) throw new InvalidDataException("Missing or repeated argument: " + name);
                string value = args[++i];
                switch (name)
                {
                    case "--max-rooms" when int.TryParse(value, out int r): maxRooms = r; break;
                    case "--max-decisions" when int.TryParse(value, out int d): maxDecisions = d; break;
                    case "--max-steps" when int.TryParse(value, out int st): maxSteps = st; break;
                    case "--stop-after": stopAfter = value; break;
                    case "--game-data": data = value; break;
                    case "--seed": seed = value; break;
                    case "--character": character = value; break;
                    case "--unlocks": unlocks = value; break;
                    case "--tier": tier = value; break;
                    case "--ascension" when int.TryParse(value, out int a): ascension = a; break;
                    case "--timeout-seconds" when int.TryParse(value, out int t): seconds = t; break;
                    default: throw new InvalidDataException("Unknown or invalid argument: " + name);
                }
            }
            if (string.IsNullOrWhiteSpace(data)) throw new InvalidDataException("--game-data is required.");
            if (string.IsNullOrWhiteSpace(seed) || ascension is < 0 or > 10 || seconds is < 1 or > 86400)
                throw new InvalidDataException("Seed must be nonempty, ascension 0..10, timeout 1..86400 seconds.");
            if (character is not ("Ironclad" or "Silent" or "Regent" or "Necrobinder" or "Defect"))
                throw new InvalidDataException("Unknown character: " + character);
            if (unlocks != "neow-epoch-v1") throw new InvalidDataException("Unknown unlock profile: " + unlocks);
            if (tier is not ("Low" or "Medium" or "High" or "ExtraHigh"))
                throw new InvalidDataException("Unknown tier: " + tier);
            if (maxRooms is < 1 or > 100 || maxDecisions < 1 || maxSteps < 1) throw new InvalidDataException("Invalid room/decision/step budget.");
            if (stopAfter is not ("first-combat" or "next-room" or "rooms")) throw new InvalidDataException("Unknown stop-after: " + stopAfter);
            using CancellationTokenSource cancellation = new();
            void Cancel(object? sender, ConsoleCancelEventArgs eventArgs) { eventArgs.Cancel = true; cancellation.Cancel(); }
            Console.CancelKeyPress += Cancel;
            FirstCombatResult result;
            TextWriter output = Console.Out;
            try
            {
                Console.SetOut(Console.Error); // Native assembly initialization can print diagnostics.
                result = FirstCombat.Run(new(data, seed, character, ascension, unlocks, tier, seconds, StopAfter: stopAfter, MaxRooms: maxRooms, MaximumDecisions: maxDecisions, MaximumSteps: maxSteps), cancellation.Token);
            }
            finally { Console.SetOut(output); Console.CancelKeyPress -= Cancel; }
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return result.Milestone is "first-combat-complete" or "next-room-entered" or "room-budget-reached" ? 0
                : result.Status == "Timeout" ? 4 : result.Status == "Cancelled" ? 130 : 1;
        }
        catch (InvalidDataException error) { Console.Error.WriteLine(error.Message); return 2; }
        catch (FileNotFoundException error) { Console.Error.WriteLine(error.Message); return 2; }
    }
}
