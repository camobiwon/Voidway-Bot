using System.Diagnostics.CodeAnalysis;

namespace Voidway.Modules.ModIO.DataTypes;

[SuppressMessage("ReSharper", "InvalidXmlDocComment")]
public enum PersistentArgumentType
{
    /// <summary>Type not set.</summary>
    None,

    // Uses _Int 0 or 1 as bool.
    /// <summary><see cref="bool"/>.</summary>
    Bool,

    // Uses _String.
    /// <summary><see cref="string"/>.</summary>
    String,

    // Uses _Int.
    /// <summary><see cref="int"/>.</summary>
    Int,

    // Uses _Int for the value and _String for the assembly qualified name of the type.
    /// <summary>Any kind of <see cref="System.Enum"/>.</summary>
    Enum,

    // Uses _X.
    /// <summary><see cref="float"/>.</summary>
    Float,

    // Uses _X and _Y.
    /// <summary><see cref="UnityEngine.Vector2"/>.</summary>
    Vector2,

    // Uses _X, _Y, and _Z.
    /// <summary><see cref="UnityEngine.Vector3"/>.</summary>
    Vector3,

    // Uses _X, _Y, _Z, and _W.
    /// <summary><see cref="UnityEngine.Vector4"/>.</summary>
    Vector4,

    // Uses _X, _Y, and _Z to store the euler angles.
    /// <summary><see cref="UnityEngine.Quaternion"/>.</summary>
    Quaternion,

    // Uses _X, _Y, _Z, and _W as RGBA.
    /// <summary><see cref="UnityEngine.Color"/>.</summary>
    Color,

    // Uses _Int to hold the RGBA bytes.
    /// <summary><see cref="UnityEngine.Color32"/>.</summary>
    Color32,

    // Uses _X, _Y, _Z, and _W as X, Y, Width, Height.
    /// <summary><see cref="UnityEngine.Rect"/>.</summary>
    Rect,

    // Uses _Object for the value and _String for the assembly qualified name of the type.
    /// <summary><see cref="UnityEngine.Object"/>.</summary>
    Object,

    // Uses _Int for the index of the target parameter.
    // If the type is a simple PersistentArgumentType (not Object or Enum), it is cast to a float and stored in _X.
    // Otherwise, the assembly qualified name of the type is stored in _String.
    /// <summary>The value of a parameter passed to the event.</summary>
    Parameter,

    // Uses _Int for the index of the target call.
    // If the type is a simple PersistentArgumentType (not Object or Enum), it is cast to a float and stored in _X.
    // Otherwise, the assembly qualified name of the type is stored in _String.
    /// <summary>The return value by a previous <see cref="PersistentCall"/>.</summary>
    ReturnValue,
}