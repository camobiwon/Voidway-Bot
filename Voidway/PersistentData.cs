using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Voidway;

internal class PersistentData
{
    public static event Action? PersistentDataChanged;
    public static PersistentData values;
    private const string PD_PATH = "./persistentData.json";

    // values
    public List<string> filenameFlagList = [ ".*epstein.*", ".*school.*" ];
    
    // plumbing
    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData())); // mmm triple parenthesis, v nice
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WritePersistentData();

        ReadPersistentData();
        WritePersistentData();
    }

    public static void OutputRawJSON()
    {
        Console.WriteLine(File.ReadAllText(PD_PATH));
    }

    [MemberNotNull(nameof(values))]
    public static void ReadPersistentData()
    {
        string configText = File.ReadAllText(PD_PATH);
        values = JsonConvert.DeserializeObject<PersistentData>(configText) ?? new PersistentData();
        Logger.Put($"Read persistent data from disk.", LogType.Debug);
    }

    public static void WritePersistentData()
    {
        Logger.Put($"Writing persistent data to disk.", LogType.Debug);
        File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(values));
        PersistentDataChanged?.Invoke();
    }
}