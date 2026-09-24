#if INNO_ENGINE_VALIDATION
using Inno.Rendering;
#else
using InnoEngine.Rendering;
#endif

namespace Inno.Canvas;

/// <summary>
/// Publishes the stable protocols owned by the Canvas rendering plugin.
/// </summary>
public static class CanvasIds
{
    internal const int presentationOrder = 100000;
    internal const string defaultMaterialPath = "Materials/Canvas.imaterial";
    internal const string defaultPipelinePath = "Pipelines/Canvas.irenderpipeline";

    /// <summary>Gets the Canvas render pipeline extension identity.</summary>
    public const string pipeline = "inno.canvas.pipeline";

    /// <summary>Gets the automatic Canvas render request provider identity.</summary>
    public const string requestProvider = "inno.canvas.request-provider";

    /// <summary>Gets the Canvas shader creation template identity.</summary>
    public const string shaderTemplate = "inno.canvas.shader-template";

    /// <summary>Gets the Canvas pipeline asset creation template identity.</summary>
    public const string pipelineCreation = "inno.canvas.asset-create.pipeline";

    /// <summary>Gets the material contract consumed by the Canvas pipeline.</summary>
    public static ShaderContractId materialContract => new("inno.canvas.material");

    /// <summary>Gets the premultiplied-alpha pass role consumed by the Canvas pipeline.</summary>
    public static ShaderPassRoleId premultipliedRole => new("inno.canvas.premultiplied");
}
