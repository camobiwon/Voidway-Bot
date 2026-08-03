using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Voidway.Modules.ModIO.DataTypes;

file enum EventType
{
    NotAnEvent,
    UltEvent,
    UnityEvent
}

file class Helper
{
    
    public static EventType ClassifyFieldType(AssetTypeValueField field)
    {
        if (field.Children.Any(c => c.TypeName == "PersistentCall"))
            return EventType.UltEvent;
        if (field.Children.Any(c => c.TypeName == "PersistentCallGroup"))
            return EventType.UnityEvent;

        return EventType.NotAnEvent;
    }
}


public static class BundleReading
{
    public static BundleScanResults VisitBundles(AssetsManager manager)
    {
        BundleScanResults results = new(manager.Bundles.Count, []);
        foreach (var bundleFileInst in manager.Bundles)
        {
            var assetsFileInst = manager.LoadAssetsFileFromBundle(bundleFileInst, 0, true);

            var gameObjects = assetsFileInst.file.GetAssetsOfType(AssetClassID.GameObject);
            
            results.gameObjects.AddRange(VisitGameObjects(gameObjects, manager, assetsFileInst));
        }

        return results;
    }
    
    private static IEnumerable<GameObjectData> VisitGameObjects(IEnumerable<AssetFileInfo> gameObjectInfos, AssetsManager mgr, AssetsFileInstance assetsFileInstance)
    {
        foreach (var gameObjectInfo in gameObjectInfos)
        {
            var gameObjectData = mgr.GetBaseField(assetsFileInstance, gameObjectInfo);
            
            var componentPairArray = gameObjectData["m_Component.Array"];
            
            // We only care about the components that have events on them
            var componentList = VisitComponents(componentPairArray, mgr, assetsFileInstance).Where(c => c.events.Count > 0).ToList();

            yield return new GameObjectData(gameObjectInfo.PathId, gameObjectData["m_Name"].AsString, componentList);
        }
    }
    
    private static IEnumerable<ComponentData> VisitComponents(IEnumerable<AssetTypeValueField> componentPairs, AssetsManager mgr, AssetsFileInstance assetFileInstance)
    {
        foreach (var componentPair in componentPairs)
        {
            var componentPtr = componentPair["component"];
            var componentWithExtInfo = mgr.GetExtAsset(assetFileInstance, componentPtr);

            var events = VisitFieldsForEvents(componentWithExtInfo.baseField);
            yield return new ComponentData(componentWithExtInfo.info.PathId, events.ToList());
        }
    }


    private static IEnumerable<EventData> VisitFieldsForEvents(IEnumerable<AssetTypeValueField> fields)
    {
        foreach (var field in fields)
        {
            switch (Helper.ClassifyFieldType(field))
            {
                case EventType.NotAnEvent:
                    continue;
                case EventType.UltEvent:
                    var ultEventCalls = field["_PersistentCalls.Array"];
                    
                    yield return new EventData(VisitUltEventCalls(ultEventCalls).ToList());
                    break;
                case EventType.UnityEvent:
                    var unityEventCalls = field["m_PersistentCalls.Array"];
                    
                    yield return new EventData(VisitUnityEventCalls(unityEventCalls).ToList());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            
        }
    }

    private static IEnumerable<PersistentCallData> VisitUltEventCalls(IEnumerable<AssetTypeValueField> persistentCalls)
    {
        foreach (var persistentCall in persistentCalls)
        {
            string methodName = persistentCall["_MethodName"].AsString;

            var arguments = persistentCall["_PersistentArguments.Array"];

            // only used as an "instanced or static" hint for now. May eventually be traversed to get asset name
            long pathId = persistentCall["_Target.m_PathID"].AsLong; 
            
            yield return new PersistentCallData(methodName, pathId == 0 ? null : $"[Asset @ PathID {pathId}]", VisitUltEventArguments(arguments).ToList());
        }
    }

    private static IEnumerable<PersistentArgumentData> VisitUltEventArguments(IEnumerable<AssetTypeValueField> parameters)
    {
        foreach (var parameter in parameters)
        {
            // Don't care about deserializing _Object, it links directly to a MonoBehaviour or UnityObject.
            // Handling the edge cases needed to traversing to its GameObject is too much work.
            // (Every component's m_Name field is serialized as an empty string and is overridden with the GO's name at runtime)
            int _Type = parameter["_Type"].AsInt;
            int _Int = parameter["_Int"].AsInt;
            string _String = parameter["_String"].AsString;
            float _X = parameter["_X"].AsFloat;
            float _Y = parameter["_Y"].AsFloat;
            float _Z = parameter["_Z"].AsFloat;
            float _W = parameter["_W"].AsFloat;

            yield return PersistentArgumentData.FromSerializedArgumentData(_Type, _Int, _String, _X, _Y, _Z, _W);
        }
    }

    private static IEnumerable<PersistentCallData> VisitUnityEventCalls(IEnumerable<AssetTypeValueField> persistentCalls)
    {
        foreach (var persistentCall in persistentCalls)
        {
            string targetAssemblyTypeName = persistentCall["m_TargetAssemblyTypeName"].AsString;
            string methodName = persistentCall["m_MethodName"].AsString;
            
            // this determines the parameter type. I don't know why it's on here and not the ArgumentCache.
            int mode = persistentCall["m_Mode"].AsInt;

            // UnityEvents can only serialize calls to parameterless or single-parameter methods
            // ... yet they still name this member "m_ArgumentS", pluralized.
            // I don't get it, man.
            var argumentCache = persistentCall["m_Arguments"];

            // only used as an "instanced or static" hint for now. May eventually be traversed to get asset name
            long pathId = persistentCall["m_Target.m_PathID"].AsLong;
            
            yield return new PersistentCallData(methodName + " from " + targetAssemblyTypeName, pathId == 0 ? null : $"[Asset @ PathID {pathId}]", VisitUnityEventArgument(argumentCache, mode).ToList());
        }
    }

    private static IEnumerable<PersistentArgumentData> VisitUnityEventArgument(AssetTypeValueField argumentCache, int mode)
    {
        long objArgPath = argumentCache["m_ObjectArgument.m_PathID"].AsLong;
        string objArgAsmTypeName = argumentCache["m_ObjectArgumentAssemblyTypeName"].AsString;
        int intArg = argumentCache["m_IntArgument"].AsInt;
        float floatArg = argumentCache["m_FloatArgument"].AsFloat;
        string stringArg = argumentCache["m_StringArgument"].AsString;
        bool boolArg = argumentCache["m_BoolArgument"].AsBool;

        yield return PersistentArgumentData.FromSerializedArgumentData(mode, $"[Asset @ PathID {objArgPath}]", objArgAsmTypeName, intArg, floatArg, stringArg, boolArg);
    }
}