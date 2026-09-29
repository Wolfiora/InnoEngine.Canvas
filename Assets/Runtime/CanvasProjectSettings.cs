using System;

using InnoEngine.Reflection;
using InnoEngine.Serialization;
using InnoEngine.Settings;

namespace Inno.Canvas;

/// <summary>
/// Defines the document density of one Canvas world unit.
/// </summary>
[StableTypeId("e1370997-4723-4cd7-b713-ff52cd43e1f0")]
[ProjectSettingDefinition("inno.canvas.layout")]
public sealed class CanvasProjectSettings : ISerializable
{
    private float m_logicalPixelsPerWorldUnit = 100f;

    /// <summary>
    /// Gets the stable project setting identity.
    /// </summary>
    public static ProjectSettingId id => new("inno.canvas.layout");

    /// <summary>
    /// Gets or sets the RML layout units represented by one world unit.
    /// </summary>
    [SerializableProperty]
    public float logicalPixelsPerWorldUnit
    {
        get => m_logicalPixelsPerWorldUnit;
        set
        {
            if (!float.IsFinite(value) || value <= 0f || value > 100000f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_logicalPixelsPerWorldUnit = value;
        }
    }
}
