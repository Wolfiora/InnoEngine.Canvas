using System;

#if INNO_ENGINE_VALIDATION
using Inno.Rendering;
using Inno.UI;
#else
using InnoEngine.Rendering;
using InnoEngine.UI;
#endif

namespace Inno.Canvas;

internal sealed class CanvasRenderFrame
{
    internal static RenderDataChannelId channel => new("inno.canvas.frame");

    internal CanvasRenderFrame(
        UiRenderFrame ui,
        MaterialAsset material,
        string resourceScope)
    {
        this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
        this.material = material ?? throw new ArgumentNullException(nameof(material));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceScope);
        this.resourceScope = resourceScope;
    }

    internal UiRenderFrame ui { get; }

    internal MaterialAsset material { get; }

    internal string resourceScope { get; }

    internal RenderFrameData ToRenderFrameData()
    {
        var data = new RenderFrameData();
        data.Set(channel, this);
        return data;
    }
}
