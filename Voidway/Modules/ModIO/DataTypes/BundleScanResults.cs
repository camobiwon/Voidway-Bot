using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Voidway.Modules.ModIO.DataTypes;

public record BundleScanResults(int bundleCount, List<GameObjectData> gameObjects)
{
    public List<string> GetUniqueFlags()
    {
        HashSet<string> flags = [];
        var (flaggedGos, flaggedCalls, flaggedArgs) = GetFlaggedData();

        foreach (var goFlag in flaggedGos.SelectMany(go => go.flags))
            flags.Add(goFlag);
        foreach (var callFlag in flaggedCalls.SelectMany(call => call.flags))
            flags.Add(callFlag);
        foreach (var argFlag in flaggedArgs.SelectMany(arg => arg.flags))
            flags.Add(argFlag);

        return flags.ToList();
    }
    
    public override string ToString()
    {
        return $"{bundleCount} bundle(s) with {gameObjects.Count} GameObjects:\n" +
               string.Join("\n============================================\n", gameObjects);
    }
    
    public (IEnumerable<GameObjectData>, IEnumerable<PersistentCallData>, IEnumerable<PersistentArgumentData>) GetFlaggedData()
    {
        List<GameObjectData> flaggedGos = [];
        List<PersistentCallData> calls = [];
        List<PersistentArgumentData> args = [];
        foreach (var gameObject in gameObjects)
        {
            if (gameObject.flags.Count > 0)
                flaggedGos.Add(gameObject);
                
            var (flaggedCalls, flaggedArgs) = gameObject.GetFlaggedData();
            calls.AddRange(flaggedCalls);
            args.AddRange(flaggedArgs);
        }
        
        return (flaggedGos , calls, args);
    }

    public string GetFlagReport()
    {
        const string TWENTY_EQUALS = "====================";
                
        var flaggedItems = GetFlaggedData();

        StringBuilder flagResults = new();

        flagResults.AppendLine($"{TWENTY_EQUALS} {flaggedItems.Item1.Count()} flagged GameObjects {TWENTY_EQUALS}");
        foreach (var goData in flaggedItems.Item1)
        {
            flagResults.AppendLine($"Flags: {string.Join(", ", goData.flags)}");
            flagResults.AppendLine(Logger.EnsureShorterThan(goData.ToString(), 150));
        }
                
        flagResults.AppendLine($"{TWENTY_EQUALS} {flaggedItems.Item2.Count()} flagged call(s) {TWENTY_EQUALS}");
        foreach (var callData in flaggedItems.Item2)
        {
            
            flagResults.AppendLine($"Flags: {string.Join(", ", callData.flags)}");
            flagResults.AppendLine(Logger.EnsureShorterThan(callData.ToString(), 150));
        }
                
        flagResults.AppendLine($"{TWENTY_EQUALS} {flaggedItems.Item3.Count()} flagged arg(s) {TWENTY_EQUALS}");
        foreach (var argData in flaggedItems.Item3)
        {
            flagResults.AppendLine($"Flags: {string.Join(", ", argData.flags)}");
            flagResults.AppendLine(Logger.EnsureShorterThan(argData.ToString(), 150));
        }
        
        return flagResults.ToString();
    }
}

public record GameObjectData
{
    public List<string> flags = [];

    public GameObjectData(long PathId, string Name, List<ComponentData> Components)
    {
        pathId = PathId;
        name = Name;
        components = Components;
        
        
        foreach (var regex in ModfileScanning.EventFlagRegexes)
        {
            if (!regex.IsMatch(name))
                continue;
            flags.Add(regex.ToString());
        }

        if (name.Length >= PersistentData.values.bundleStringFlagThreshold)
        {
            flags.Add($"Length {name.Length} >= flag length {PersistentData.values.bundleStringFlagThreshold}");
        }
    }

    public long pathId { get; init; }
    public string name { get; init; }
    public List<ComponentData> components { get; init; }
    
    public override string ToString()
    {
        string flagsStr = flags.Count == 0 ? "" : $", {flags.Count} flag(s): {string.Join(", ", flags)}";
        return $"GameObject (\"{name}\"{flagsStr}) @ PathID {pathId}, with {components.Count} component(s):\n\t" +
               string.Join("\n\t", components);
    }
    
    public (IEnumerable<PersistentCallData>, IEnumerable<PersistentArgumentData>) GetFlaggedData()
    {
        List<PersistentCallData> calls = [];
        List<PersistentArgumentData> args = [];
        foreach (var eventData in components)
        {
            var (flaggedCalls, flaggedArgs) = eventData.GetFlaggedData();
            calls.AddRange(flaggedCalls);
            args.AddRange(flaggedArgs);
        }
        return (calls, args);
    }

    [SuppressMessage("ReSharper", "ParameterHidesMember")]
    public void Deconstruct(out long pathId, out string name, out List<ComponentData> components)
    {
        pathId = this.pathId;
        name = this.name;
        components = this.components;
    }
}

// No use having a name field on here, every script just shows up as "MonoBehaviour" (awesome serialization, Unity)
// Things are bound via PathIDs though, so that's useful.
public record ComponentData(long pathId, List<EventData> events)
{
    public override string ToString()
    {
        if (events.Count == 0)
            return $"Component @ PathID {pathId} (eventless).";
        
        return $"Component with {events.Count} event(s) @ PathID {pathId}:\n\t\t" +
               string.Join("\n\t\t", events.Select((e, idx) => $"Event number {idx + 1}: {e}"));
    }

    public (IEnumerable<PersistentCallData>, IEnumerable<PersistentArgumentData>) GetFlaggedData()
    {
        List<PersistentCallData> calls = [];
        List<PersistentArgumentData> args = [];
        foreach (var eventData in events)
        {
            var (flaggedCalls, flaggedArgs) = eventData.GetFlaggedData();
            calls.AddRange(flaggedCalls);
            args.AddRange(flaggedArgs);
        }
        return (calls, args);
    }
}

public record EventData(List<PersistentCallData> calls)
{
    public override string ToString()
    {
        if (calls.Count == 0)
            return "Empty event";
        
        return $"Event with {calls.Count} calls:\n\t\t\t" +
               string.Join("\n\t\t\t", calls.Select((call, idx) => $"Call idx {idx}: {call}" ));
    }

    public (IEnumerable<PersistentCallData>, IEnumerable<PersistentArgumentData>) GetFlaggedData()
    {
        var flaggedCalls = calls.Where(c => c.flags.Count > 0 || c.arguments.Any(a => a.flags.Count > 0));
        var flaggedArgs = calls.SelectMany(c => c.GetFlaggedArguments());
        return (flaggedCalls, flaggedArgs);
    }
}

public record PersistentCallData
{
    public readonly List<string> flags = [];

    public PersistentCallData(string Method, string? InstanceIfAny, List<PersistentArgumentData> Arguments)
    {
        method = Method;
        instanceIfAny = InstanceIfAny;
        arguments = Arguments;
        
        foreach (var regex in ModfileScanning.EventFlagRegexes)
        {
            if (!regex.IsMatch(method))
                continue;
            
            flags.Add(regex.ToString());
        }

        if (method.Length >= PersistentData.values.bundleStringFlagThreshold)
        {
            flags.Add($"Length {method.Length} >= flag length {PersistentData.values.bundleStringFlagThreshold}");
        }
    }

    public string method { get; init; }
    public string? instanceIfAny { get; init; }
    public List<PersistentArgumentData> arguments { get; init; }
    
    public override string ToString()
    {
        string methodDesc = string.IsNullOrEmpty(instanceIfAny)
            ? $"static method {method}"
            : $"instance method {method} on {instanceIfAny}";
        
        if (flags.Count > 0)
            methodDesc = "[!!!] flagged " + methodDesc + $", {flags.Count} flag(s): {string.Join(", ", flags)}";

        if (arguments.Count == 0)
        {
            return $"Call parameterless {methodDesc}";
        }
        
        return $"Call {methodDesc}, passing in {arguments.Count} argument(s):\n\t\t\t\t" +
               string.Join("\n\t\t\t\t",  arguments.Select((arg, idx) => $"Argument idx {idx}: {arg}"));
    }

    public IEnumerable<PersistentArgumentData> GetFlaggedArguments()
    {
        return arguments.Where(arg => arg.flags.Count != 0);
    }
    
    [SuppressMessage("ReSharper", "ParameterHidesMember")]
    public void Deconstruct(out string method, out string? instanceIfAny, out List<PersistentArgumentData> arguments)
    {
        method = this.method;
        instanceIfAny = this.instanceIfAny;
        arguments = this.arguments;
    }
}

public record PersistentArgumentData(string description)
{
    public List<string> flags = [];
    
    // UnityEvents don't serialize multi-argument calls.
    // m_mode comes from the UnityEvent's PersistentCall
    public static PersistentArgumentData FromSerializedArgumentData(int _mode, string objArg, string objArgTypeName, int intArgument, float floatArgument, string stringArgument, bool boolArgument)
    {
        PersistentListenerMode mode = (PersistentListenerMode)_mode;
        string description = "";
        switch (mode)
        {
            case PersistentListenerMode.EventDefined:
                description = "<Dynamic call from .Invoke(...)>";
                break;
            case PersistentListenerMode.Void:
                description = "<Parameterless method, ignore>";
                break;
            case PersistentListenerMode.Object:
                description = $"Object argument of type {objArgTypeName} ({objArg})";
                break;
            case PersistentListenerMode.Int:
                description = $"Integer argument ({intArgument})";
                break;
            case PersistentListenerMode.Float:
                description = $"Float argument ({floatArgument})";
                break;
            case PersistentListenerMode.String:
                description = $"String argument ({stringArgument})";
                break;
            case PersistentListenerMode.Bool:
                description = $"Bool argument ({boolArgument})";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return FromDescription(description);
    }
    
    // From UltEvents
    public static PersistentArgumentData FromSerializedArgumentData(int Type, int Int, string? String, float X, float Y, float Z, float W)
    {
        PersistentArgumentType type = (PersistentArgumentType)Type;
        string description = "";

        // if (type == PersistentArgumentType.Parameter)
        // {
        //     description += $"[Parameter idx {Int} passed to event] ";
        //     type = (PersistentArgumentType)(int)X;
        // }
        // if (type == PersistentArgumentType.ReturnValue)
        // {
        //     description += $"[Return value from call idx {Int}] ";
        //     type = (PersistentArgumentType)(int)X;
        // }
        
        switch (type)
        {
            case PersistentArgumentType.None:
                description += "Untyped argument (this should not appear)";
                break;
            case PersistentArgumentType.Bool:
                description += $"Bool argument ({(Int == 0 ? "false" : "true")})";
                break;
            case PersistentArgumentType.String:
                description += $"String argument ({String})";
                break;
            case PersistentArgumentType.Int:
                description += $"Integer argument ({Int})";
                break;
            case PersistentArgumentType.Enum:
                description += $"Enum argument (value represented by {Int} from enum {String})";
                break;
            case PersistentArgumentType.Float:
                description += $"Float argument ({X})";
                break;
            case PersistentArgumentType.Vector2:
                description += $"Vector2 argument ({X}, {Y})";
                break;
            case PersistentArgumentType.Vector3:
                description += $"Vector3 argument ({X}, {Y}, {Z})";
                break;
            case PersistentArgumentType.Vector4:
                description += $"Vector4 argument ({X}, {Y}, {Z}, {W})";
                break;
            case PersistentArgumentType.Quaternion:
                description += $"Quaternion argument (Euler {X}, {Y}, {Z})";
                break;
            case PersistentArgumentType.Color:
                description += $"Color argument (R {X}, G {Y}, B {Z}, A {W})";
                break;
            case PersistentArgumentType.Color32:
                int colorBits = Int;
                byte r, g, b, a;
                a = unchecked((byte)colorBits);
                colorBits >>= 8;
                b = unchecked((byte)colorBits);
                colorBits >>= 8;
                g = unchecked((byte)colorBits);
                colorBits >>= 8;
                r = unchecked((byte)colorBits);
                description += $"Color32 argument (R {r}, G {g}, B {b}, A {a}, or #{Int:X})";
                break;
            case PersistentArgumentType.Rect:
                description += $"Rect argument (X {X}, Y {Y}, Width {Z}, Height {W})";
                break;
            case PersistentArgumentType.Object:
                description += $"Object reference argument of type {String}";
                break;
            case PersistentArgumentType.Parameter:
                description = $"[Parameter idx {Int} passed to event]";
                break;
            case PersistentArgumentType.ReturnValue:
                description = $"[Return value from call idx {Int}]";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return FromDescription(description);
    }

    private static PersistentArgumentData FromDescription(string description)
    {
        var ret = new PersistentArgumentData(description);
        
        foreach (var regex in ModfileScanning.EventFlagRegexes)
        {
            if (!regex.IsMatch(description))
                continue;
            
            ret.flags.Add(regex.ToString());
        }

        if (description.Length >= PersistentData.values.bundleStringFlagThreshold)
        {
            ret.flags.Add($"Length {description.Length} >= flag length {PersistentData.values.bundleStringFlagThreshold}");
        }

        return ret;
    }
    
    public override string ToString()
    {
        string flagsStr = flags.Count == 0 ? "" : $", {flags.Count} flag(s): {string.Join(", ", flags)}";
        return $"{(flags.Count != 0 ? "[!!!] FLAGGED " : "")}{description}{flagsStr}";
    }
}