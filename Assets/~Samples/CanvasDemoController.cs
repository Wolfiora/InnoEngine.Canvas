#if INNO_ENGINE_VALIDATION
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;
using Inno.UI;
#else
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEngine.UI;
#endif

namespace Inno.Canvas.Samples;

/// <summary>Demonstrates button events, HUD text changes, and an RCSS fade.</summary>
[StableTypeId("600db367-c503-438a-a2c4-bf3da741e9a9")]
public sealed class CanvasDemoController : GameBehavior
{
    private int m_clicks;
    private bool m_faded;

    /// <inheritdoc />
    protected override void Update()
    {
        if (!gameObject.TryGetComponent(out Canvas? canvas) || canvas is null || !canvas.isReady)
            return;
        foreach (UiEvent uiEvent in canvas.DrainEvents())
        {
            if (uiEvent.type != UiEventType.Click || uiEvent.targetId != "launch")
                continue;
            m_clicks++;
            m_faded = !m_faded;
            canvas.SetText("status", $"Clicks: {m_clicks}");
            canvas.SetClass("message", "faded", m_faded);
        }
    }
}
