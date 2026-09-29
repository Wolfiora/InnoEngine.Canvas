using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEngine.UI;
using System;

namespace Inno.Canvas.Samples;

/// <summary>
/// Demonstrates button events, HUD text changes, and an RCSS fade.
/// </summary>
[StableTypeId("600db367-c503-438a-a2c4-bf3da741e9a9")]
public sealed class CanvasDemoController : GameBehavior
{
    private int m_clicks;
    private bool m_faded;
    private IDisposable? m_subscription;

    /// <inheritdoc />
    protected override void Start()
    {
        if (!gameObject.TryGetComponent(out Canvas? canvas) || canvas is null)
            return;
        m_subscription = canvas.Listen(uiEvent =>
        {
            if (uiEvent.type != UiEventType.Click || uiEvent.targetId != "launch")
                return;
            m_clicks++;
            m_faded = !m_faded;
            canvas.SetText("status", $"Clicks: {m_clicks}");
            canvas.SetClass("message", "faded", m_faded);
        });
    }

    /// <inheritdoc />
    protected override void OnDestroy() => m_subscription?.Dispose();
}
