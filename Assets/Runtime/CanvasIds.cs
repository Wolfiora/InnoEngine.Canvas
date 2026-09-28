using InnoEngine.Rendering;

namespace Inno.Canvas;

/// <summary>
/// Publishes the stable protocols owned by the Canvas rendering plugin.
/// </summary>
public static class CanvasIds
{
    internal const string defaultMaterialPath = "Materials/Canvas.imaterial";

    /// <summary>Gets the world-content source extension identity.</summary>
    public const string contentSource = "inno.canvas.world-content";

    /// <summary>Gets the Canvas shader creation template identity.</summary>
    public const string shaderTemplate = "inno.canvas.shader-template";

    /// <summary>Gets the material contract consumed by the Canvas pipeline.</summary>
    public static ShaderContractId materialContract => new("inno.canvas.material");

    /// <summary>Gets the premultiplied-alpha pass role consumed by the Canvas pipeline.</summary>
    public static ShaderPassRoleId premultipliedRole => new("inno.canvas.premultiplied");
}
