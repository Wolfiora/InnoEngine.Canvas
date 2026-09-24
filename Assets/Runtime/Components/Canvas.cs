using System;
using System.Collections.Generic;

#if INNO_ENGINE_VALIDATION
using AssetsApi = Inno.Assets.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Scene;
using Inno.Text;
using Inno.UI;
using UiApi = Inno.UI.UI;
#else
using AssetsApi = InnoEngine.Assets.Assets;
using InnoEngine.Assets;
using InnoEngine.Rendering;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEngine.Text;
using InnoEngine.UI;
using UiApi = InnoEngine.UI.UI;
#endif

namespace Inno.Canvas;

/// <summary>
/// Declares one full-presentation retained UI document rendered by the Canvas plugin.
/// </summary>
[StableTypeId("385f32c2-aa2f-4383-82d0-dcedb722e680")]
public sealed class Canvas : GameBehavior
{
    private static readonly IReadOnlyList<UiEvent> S_NO_EVENTS = Array.Empty<UiEvent>();
    private string m_fontFamily = "Interface";
    private int m_fontWeight = 400;
    private float m_density = 1f;
    private UiContextHandle m_context;
    private UiDocumentHandle m_loadedDocument;
    private IReadOnlyList<UiEvent> m_events = S_NO_EVENTS;

    /// <summary>Gets or sets the imported RML document displayed by this canvas.</summary>
    [SerializableProperty]
    public UiDocumentAsset? document { get; set; }

    /// <summary>Gets or sets the optional primary font registered before the document is loaded.</summary>
    [SerializableProperty]
    public FontAsset? font { get; set; }

    /// <summary>Gets or sets the CSS family name assigned to <see cref="font"/>.</summary>
    [SerializableProperty]
    public string fontFamily
    {
        get => m_fontFamily;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            m_fontFamily = value.Trim();
        }
    }

    /// <summary>Gets or sets the style assigned to <see cref="font"/>.</summary>
    [SerializableProperty]
    public TextFontStyle fontStyle { get; set; }

    /// <summary>Gets or sets the CSS weight assigned to <see cref="font"/>.</summary>
    [SerializableProperty]
    public int fontWeight
    {
        get => m_fontWeight;
        set
        {
            if (value is < 100 or > 1000)
                throw new ArgumentOutOfRangeException(nameof(value), "Font weight must be between 100 and 1000.");
            m_fontWeight = value;
        }
    }

    /// <summary>Gets or sets whether the primary font is also a fallback face.</summary>
    [SerializableProperty]
    public bool fontFallback { get; set; }

    /// <summary>Gets or sets the render pipeline asset configured for <see cref="CanvasRenderPipeline"/>.</summary>
    [SerializableProperty]
    public RenderPipelineAsset? pipeline { get; set; }

    /// <summary>Gets or sets the premultiplied Canvas material used to draw generated geometry.</summary>
    [SerializableProperty]
    public MaterialAsset? material { get; set; }

    /// <summary>Gets or sets the logical-to-physical pixel density used by RmlUi layout.</summary>
    [SerializableProperty]
    public float density
    {
        get => m_density;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value), "Canvas density must be finite and positive.");
            m_density = value;
        }
    }

    /// <summary>Gets or sets the ordering offset applied after ordinary presentation requests.</summary>
    [SerializableProperty]
    public int order { get; set; }

    /// <summary>Gets whether the plugin currently owns a live UI context for this component.</summary>
    public bool isReady => m_context.isValid && m_loadedDocument.isValid;

    /// <summary>Gets the immutable DOM events produced by the most recent Canvas update.</summary>
    public IReadOnlyList<UiEvent> events => m_events;

    /// <summary>Replaces an element's children with escaped plain text.</summary>
    /// <param name="elementId">The target element identifier.</param>
    /// <param name="text">The replacement text.</param>
    /// <returns><see langword="true"/> when the active document contained the target element.</returns>
    /// <exception cref="InvalidOperationException">The Canvas has not created its runtime document.</exception>
    public bool SetText(string elementId, string text)
        => UiApi.SetText(RequireContext(), RequireDocument(), elementId, text);

    /// <summary>Replaces an element's children with explicitly language-tagged document content.</summary>
    /// <param name="elementId">The target element identifier.</param>
    /// <param name="content">The replacement language-tagged fragment.</param>
    /// <returns><see langword="true"/> when the active document contained the target element.</returns>
    public bool SetContent(string elementId, UiDocumentFragment content)
        => UiApi.SetContent(RequireContext(), RequireDocument(), elementId, content);

    /// <summary>Sets one attribute on an element in the active document.</summary>
    /// <param name="elementId">The target element identifier.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The attribute value.</param>
    /// <returns><see langword="true"/> when the active document contained the target element.</returns>
    /// <exception cref="InvalidOperationException">The Canvas has not created its runtime document.</exception>
    public bool SetAttribute(string elementId, string name, string value)
        => UiApi.SetAttribute(RequireContext(), RequireDocument(), elementId, name, value);

    /// <summary>Activates or deactivates one class on an element in the active document.</summary>
    /// <param name="elementId">The target element identifier.</param>
    /// <param name="className">The class name.</param>
    /// <param name="active">Whether the class should be active.</param>
    /// <returns><see langword="true"/> when the active document contained the target element.</returns>
    /// <exception cref="InvalidOperationException">The Canvas has not created its runtime document.</exception>
    public bool SetClass(string elementId, string className, bool active)
        => UiApi.SetClass(RequireContext(), RequireDocument(), elementId, className, active);

    /// <summary>Registers one in-memory texture source for use by the active RML document.</summary>
    /// <param name="source">The source name referenced by RML.</param>
    /// <param name="texture">The immutable RGBA8 texture.</param>
    /// <exception cref="InvalidOperationException">The Canvas has not created its runtime context.</exception>
    public void RegisterTexture(string source, UiTextureData texture)
        => UiApi.RegisterTexture(RequireContext(), source, texture);

    /// <summary>Assigns the source-local Canvas pipeline and material to a newly attached component.</summary>
    protected override void Reset()
    {
        pipeline = AssetsApi.Load<RenderPipelineAsset>(AssetsApi.LocalPath(CanvasIds.defaultPipelinePath));
        material = AssetsApi.Load<MaterialAsset>(AssetsApi.LocalPath(CanvasIds.defaultMaterialPath));
    }

    internal void Bind(UiContextHandle context, UiDocumentHandle loadedDocument)
    {
        m_context = context;
        m_loadedDocument = loadedDocument;
        m_events = S_NO_EVENTS;
    }

    internal void Unbind(UiContextHandle context)
    {
        if (m_context != context)
            return;
        m_context = default;
        m_loadedDocument = default;
        m_events = S_NO_EVENTS;
    }

    internal void PublishEvents(IReadOnlyList<UiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        m_events = events.Count == 0 ? S_NO_EVENTS : new List<UiEvent>(events).AsReadOnly();
    }

    private UiContextHandle RequireContext()
        => m_context.isValid
            ? m_context
            : throw new InvalidOperationException("The Canvas runtime context is not ready.");

    private UiDocumentHandle RequireDocument()
        => m_loadedDocument.isValid
            ? m_loadedDocument
            : throw new InvalidOperationException("The Canvas runtime document is not ready.");
}
