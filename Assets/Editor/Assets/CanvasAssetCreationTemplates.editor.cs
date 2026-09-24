#if INNO_ENGINE_VALIDATION
using Inno.Editor.Assets;
using Inno.Rendering;
#else
using InnoEditor.Assets;
using InnoEngine.Rendering;
#endif

namespace Inno.Canvas;

/// <summary>Creates a Render Pipeline asset already configured for the Canvas extension.</summary>
[AssetCreationMenu(
    CanvasIds.pipelineCreation,
    "Canvas/Render Pipeline",
    ".irenderpipeline",
    "New Canvas Render Pipeline",
    groupOrder: 310,
    itemOrder: 100,
    separatorBeforeGroup: true)]
public sealed class CanvasPipelineAssetCreationTemplate : AssetCreationTemplate<RenderPipelineAsset>
{
    /// <inheritdoc />
    protected override RenderPipelineAsset CreateAsset()
        => new() { pipelineTypeId = CanvasIds.pipeline };
}
