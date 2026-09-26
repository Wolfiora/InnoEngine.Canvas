using System;
using System.Collections.Generic;

#if INNO_ENGINE_VALIDATION
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;
using Inno.UI;
using UiApi = Inno.UI.UI;
#else
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEngine.UI;
using UiApi = InnoEngine.UI.UI;
#endif

namespace Inno.Canvas;

/// <summary>
/// Declares a retained UI document on this object's transformed world plane.
/// </summary>
[StableTypeId("385f32c2-aa2f-4383-82d0-dcedb722e680")]
public sealed class Canvas : GameBehavior
{
    private static readonly IReadOnlyList<UiEvent> S_NO_EVENTS = Array.Empty<UiEvent>();
    private UiContextHandle m_context;
    private UiDocumentHandle m_loadedDocument;
    private IUiService? m_ui;
    private Guid m_documentId;
    private long m_documentVersion;
    private int m_viewportWidth;
    private int m_viewportHeight;
    private float m_density;
    private readonly List<UiEvent> m_events = [];
    private int m_referenceWidth = 800;
    private int m_referenceHeight = 450;

    /// <summary>Gets or sets the imported RML document displayed by this canvas.</summary>
    [SerializableProperty]
    public UiDocumentAsset? document { get; set; }

    /// <summary>Gets or sets the fixed horizontal RML layout extent in logical pixels.</summary>
    [SerializableProperty]
    public int referenceWidth
    {
        get => m_referenceWidth;
        set => m_referenceWidth = value is >= 1 and <= 4096
            ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Gets or sets the fixed vertical RML layout extent in logical pixels.</summary>
    [SerializableProperty]
    public int referenceHeight
    {
        get => m_referenceHeight;
        set => m_referenceHeight = value is >= 1 and <= 4096
            ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Gets whether the plugin currently owns a live UI context for this component.</summary>
    public bool isReady => m_context.isValid && m_loadedDocument.isValid;

    /// <summary>
    /// Removes and returns DOM events accumulated since the previous drain.
    /// </summary>
    /// <returns>
    /// The ordered pending events, or an empty collection when none are pending.
    /// </returns>
    public IReadOnlyList<UiEvent> DrainEvents()
    {
        if (m_events.Count == 0)
            return S_NO_EVENTS;
        UiEvent[] pending = m_events.ToArray();
        m_events.Clear();
        return pending;
    }

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

    internal (IUiService ui, UiContextHandle context, UiDocumentHandle loadedDocument) EnsureDocument(
        int width, int height, float density)
    {
        UiDocumentAsset source = document is { isMissing: false, source: not null } available
            ? available : throw new InvalidOperationException("Canvas has no usable UI document.");
        IUiService ui = UiExecutionContext.current;
        if (m_context.isValid &&
            (!ReferenceEquals(m_ui, ui) || m_documentId != source.identity.persistentId
                || m_documentVersion != source.contentVersion))
            ReleaseDocument();
        if (!m_context.isValid)
        {
            UiContextHandle context = ui.CreateContext(new UiContextOptions(
                $"Canvas/{identity.runtimeIdentity}", width, height, density));
            try
            {
                UiDocumentHandle loaded = ui.LoadDocument(context, source);
                ui.ShowDocument(context, loaded);
                m_ui = ui;
                m_context = context;
                m_loadedDocument = loaded;
                m_documentId = source.identity.persistentId;
                m_documentVersion = source.contentVersion;
                m_viewportWidth = width;
                m_viewportHeight = height;
                m_density = density;
                m_events.Clear();
            }
            catch
            {
                ui.DestroyContext(context);
                throw;
            }
        }
        else if (m_viewportWidth != width || m_viewportHeight != height || m_density != density)
        {
            ui.SetViewport(m_context, width, height, density);
            m_viewportWidth = width;
            m_viewportHeight = height;
            m_density = density;
        }
        return (ui, m_context, m_loadedDocument);
    }

    /// <inheritdoc />
    protected override void OnDestroy() => ReleaseDocument();

    private void ReleaseDocument()
    {
        if (m_ui is not null && m_context.isValid)
        {
            try { m_ui.DestroyContext(m_context); }
            catch (ObjectDisposedException) { }
        }
        m_ui = null;
        m_context = default;
        m_loadedDocument = default;
        m_events.Clear();
    }

    internal void PublishEvents(IReadOnlyList<UiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        m_events.AddRange(events);
    }

    private UiContextHandle RequireContext()
        => EnsureDocument(m_context.isValid ? m_viewportWidth : referenceWidth,
            m_context.isValid ? m_viewportHeight : referenceHeight,
            m_context.isValid ? m_density : 1f).context;

    private UiDocumentHandle RequireDocument()
        => EnsureDocument(m_context.isValid ? m_viewportWidth : referenceWidth,
            m_context.isValid ? m_viewportHeight : referenceHeight,
            m_context.isValid ? m_density : 1f).loadedDocument;
}
