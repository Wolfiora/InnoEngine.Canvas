using System;
using System.Collections.Generic;
using InnoEngine.Events;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEngine.UI;
using UiApi = InnoEngine.UI.UI;

namespace Inno.Canvas;

/// <summary>
/// Declares a retained UI document on this object's transformed world plane.
/// </summary>
[StableTypeId("385f32c2-aa2f-4383-82d0-dcedb722e680")]
public sealed class Canvas : GameBehavior
{
    private readonly EventDispatcher m_eventDispatcher = new();
    private readonly EventHub m_events;
    private UiContextHandle m_context;
    private UiDocumentHandle m_loadedDocument;
    private IUiService? m_ui;
    private Guid m_documentId;
    private long m_documentVersion;
    private int m_viewportWidth;
    private int m_viewportHeight;
    private float m_density;
    private int m_referenceWidth = 800;
    private int m_referenceHeight = 450;

    /// <summary>Creates a Canvas with an isolated Core event hub for its document interactions.</summary>
    public Canvas() => m_events = m_eventDispatcher.CreateHub();

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
    /// Subscribes to document interactions through this Canvas's Core event hub.
    /// </summary>
    /// <param name="handler">The callback invoked for each document event.</param>
    /// <param name="priority">Listener priority within this Canvas; higher values run first.</param>
    /// <returns>A token that removes the listener when disposed.</returns>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    /// <exception cref="InvalidOperationException">The Canvas has been destroyed.</exception>
    public IDisposable Listen(Action<UiEvent> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return m_events.Listen<CanvasUiEvent>(uiEvent => handler(uiEvent.value), priority);
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
    protected override void Update() => m_eventDispatcher.Flush();

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        ReleaseDocument();
        m_events.Dispose();
    }

    private void ReleaseDocument()
    {
        m_eventDispatcher.DiscardPending();
        if (m_ui is not null && m_context.isValid)
        {
            try { m_ui.DestroyContext(m_context); }
            catch (ObjectDisposedException) { }
        }
        m_ui = null;
        m_context = default;
        m_loadedDocument = default;
    }

    internal void PublishEvents(IReadOnlyList<UiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (UiEvent uiEvent in events)
            m_eventDispatcher.Enqueue(new CanvasUiEvent(uiEvent));
    }

    private UiContextHandle RequireContext()
        => EnsureDocument(m_context.isValid ? m_viewportWidth : referenceWidth,
            m_context.isValid ? m_viewportHeight : referenceHeight,
            m_context.isValid ? m_density : 1f).context;

    private UiDocumentHandle RequireDocument()
        => EnsureDocument(m_context.isValid ? m_viewportWidth : referenceWidth,
            m_context.isValid ? m_viewportHeight : referenceHeight,
            m_context.isValid ? m_density : 1f).loadedDocument;

    private sealed class CanvasUiEvent : Event
    {
        public CanvasUiEvent(UiEvent value) => this.value = value;

        public UiEvent value { get; }
    }
}
