using System.Diagnostics.CodeAnalysis;
using DSharpPlus.Entities.AuditLogs;
using Newtonsoft.Json;
using Tomlet;
using Tomlet.Models;

namespace Voidway;

[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
internal class PersistentData
{
    public static event Action? PersistentDataChanged;
    public static PersistentData values;
    private const string PD_PATH = "./persistentData.json";

    // values
    public List<string> filenameFlagList = [ ];
    public Dictionary<ulong, Dictionary<ulong, ulong>> modNoteMessages = []; // guild id -> member id -> message id  

    // guild -> day -> user -> <info>
    public Dictionary<ulong, Dictionary<DateOnly, Dictionary<ulong, int>>> observedMessages = [];
    public Dictionary<ulong, Dictionary<DateOnly, Dictionary<ulong, List<string>>>> moderationActions = [];
    
    // mod barcode (from .hash filename) -> name id
    public Dictionary<string, string> barcodesToOriginalUploaders = [];
    // In case someone tries obscuring where their mod is originally from by renaming the .hash file
    public Dictionary<string, string> hashesToOriginalBarcodes = [];
    // modFILE uint id, NOT mod id
    public List<uint> modFilesInCatalog = [];
    // via Name ID
    public List<string> trustedModders = [];
    
    // plumbing
    static PersistentData()
    {
        Console.WriteLine("Initializing persistent data storage");
        if (!File.Exists(PD_PATH))
        {
            File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(new PersistentData(), Formatting.Indented)); // mmm triple parenthesis, v nice
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
        File.WriteAllText(PD_PATH, JsonConvert.SerializeObject(values, Formatting.Indented));
        PersistentDataChanged?.Invoke();
    }
}