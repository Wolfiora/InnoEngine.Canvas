using System;
using System.Collections.Generic;
using System.Linq;

using AssetsApi = InnoEngine.Assets.Assets;
using InnoEngine.Core;
using InnoEngine.Logging;
using InnoEngine.Mathematics;
using InnoEngine.Input;
using InnoEngine.Settings;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.UI;

namespace Inno.Canvas;

/// <summary>Publishes transformed retained UI planes to any compatible rendering model.</summary>
[ViewContentSourceExtension("inno.canvas.world-content")]
public sealed class CanvasViewContentSource : IViewContentSource, IViewContentFrameSource
{
    private const int C_MAX_CONTEXT_EDGE = 4096;
    private const float C_LAYOUT_EPSILON = 0.001f;
    private readonly Dictionary<RuntimeIdentity, CanvasState> m_states = [];
    private readonly HashSet<RuntimeIdentity> m_invalidSizes = [];
    private readonly HashSet<RuntimeIdentity> m_missingDocuments = [];
    private bool m_disposed;

    /// <inheritdoc />
    public void Collect(ViewContentContext context, IViewContentSink sink)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sink);
        foreach (RuntimeIdentity key in m_states.Keys.Where(key =>
                     m_states[key].owner.isDestroyed ||
                     m_states[key].owner.identity.runtimeIdentity != key).ToArray())
        {
            m_states[key].Dispose();
            m_states.Remove(key);
            m_invalidSizes.Remove(key);
            m_missingDocuments.Remove(key);
        }
        foreach (GameScene scene in context.content.GetValues<GameScene>())
        {
            if (scene.isDestroyed)
                continue;
            foreach (GameObject owner in scene.GetObjects())
            {
                if (!owner.TryGetComponent(out Canvas? canvas)
                    || canvas is null
                    || !canvas.isActiveAndEnabled)
                    continue;
                if (canvas.identity.runtimeIdentity is not RuntimeIdentity id)
                    continue;
                if (canvas.document is not { isMissing: false, source: not null })
                {
                    RemoveState(id);
                    if (m_missingDocuments.Add(id))
                        Log.Warn($"Canvas '{owner.name}' has no usable RML document.");
                    continue;
                }
                m_missingDocuments.Remove(id);
                float pixelsPerUnit = Settings.Get<CanvasProjectSettings>(CanvasProjectSettings.id)
                    .logicalPixelsPerWorldUnit;
                Matrix transform = owner.transform.localToWorldMatrix * Matrix.CreateScale(
                    canvas.referenceWidth / pixelsPerUnit,
                    canvas.referenceHeight / pixelsPerUnit,
                    1f);
                if (!TryDescribe(canvas, context.views, transform, out CanvasLayout layout))
                {
                    RemoveState(id);
                    if (m_invalidSizes.Add(id))
                        Log.Warn($"Canvas '{owner.name}' has an invalid world size; each layout edge must be between 1 and {C_MAX_CONTEXT_EDGE} logical pixels.");
                    continue;
                }
                m_invalidSizes.Remove(id);
                CanvasState state = GetOrCreate(canvas, layout);
                state.planeTransform = transform;
                sink.Submit(new ViewContentItem(
                    owner.identity,
                    transform,
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),
                    state.drawable,
                    state));
            }
        }
    }

    /// <inheritdoc />
    public void CompleteFrame(ulong frameIndex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        foreach (CanvasState state in m_states.Values)
            state.CompleteFrame(frameIndex);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_disposed)
            return;
        foreach (CanvasState state in m_states.Values)
            state.Dispose();
        m_states.Clear();
        m_invalidSizes.Clear();
        m_missingDocuments.Clear();
        m_disposed = true;
    }

    private void RemoveState(RuntimeIdentity id)
    {
        if (!m_states.Remove(id, out CanvasState? state))
            return;
        state.Dispose();
    }

    private CanvasState GetOrCreate(Canvas canvas, CanvasLayout layout)
    {
        RuntimeIdentity id = canvas.identity.runtimeIdentity
            ?? throw new InvalidOperationException("Canvas has no live runtime identity.");
        if (m_states.TryGetValue(id, out CanvasState? existing) &&
            ReferenceEquals(existing.owner, canvas) &&
            existing.layout.density > layout.density &&
            existing.layout.logicalWidth == layout.logicalWidth &&
            existing.layout.logicalHeight == layout.logicalHeight)
            layout = existing.layout;
        (IUiService ui, UiContextHandle handle, UiDocumentHandle document) =
            canvas.EnsureDocument(layout.width, layout.height, layout.density);
        if (m_states.TryGetValue(id, out CanvasState? state))
        {
            if (ReferenceEquals(state.owner, canvas)
                && state.documentId == canvas.document!.identity.persistentId
                && state.documentVersion == canvas.document.contentVersion
                && state.context == handle)
            {
                state.layout = layout;
                return state;
            }
            state.Dispose();
            m_states.Remove(id);
        }
        MaterialAsset? material = AssetsApi.Load<MaterialAsset>(AssetsApi.LocalPath(CanvasIds.defaultMaterialPath));
        if (material is null || material.isMissing)
            throw new InvalidOperationException("The source-local Canvas material is unavailable.");
        state = new CanvasState(canvas, ui, handle, document, layout,
            new CanvasViewDrawable($"inno.canvas.{id}", material),
            canvas.document!.identity.persistentId, canvas.document.contentVersion);
        m_states.Add(id, state);
        return state;
    }

    private static bool TryDescribe(Canvas canvas, IReadOnlyList<RenderView> views, Matrix transform, out CanvasLayout layout)
    {
        float worldWidth = Vector3.TransformNormal(Vector3.RIGHT, transform).Length();
        float worldHeight = Vector3.TransformNormal(Vector3.UP, transform).Length();
        float logicalWidth = canvas.referenceWidth;
        float logicalHeight = canvas.referenceHeight;
        if (!float.IsFinite(logicalWidth) || !float.IsFinite(logicalHeight)
            || logicalWidth < 1f || logicalHeight < 1f
            || logicalWidth > C_MAX_CONTEXT_EDGE || logicalHeight > C_MAX_CONTEXT_EDGE
            || !float.IsFinite(worldWidth) || !float.IsFinite(worldHeight)
            || worldWidth <= 0.000001f || worldHeight <= 0.000001f)
        {
            layout = default;
            return false;
        }
        float density = 1f;
        foreach (RenderView view in views)
        {
            float projectedWidth = ProjectedLength(view, transform, new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f));
            float projectedHeight = ProjectedLength(view, transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(0f, 0.5f, 0f));
            density = MathF.Max(density,
                MathF.Max(projectedWidth / logicalWidth, projectedHeight / logicalHeight));
        }
        density = MathF.Ceiling(MathF.Min(8f, density) * 4f - C_LAYOUT_EPSILON) * 0.25f;
        density = MathF.Min(density, MathF.Min(C_MAX_CONTEXT_EDGE / logicalWidth, C_MAX_CONTEXT_EDGE / logicalHeight));
        layout = new CanvasLayout(
            Math.Clamp((int)MathF.Ceiling(logicalWidth - C_LAYOUT_EPSILON), 1, C_MAX_CONTEXT_EDGE),
            Math.Clamp((int)MathF.Ceiling(logicalHeight - C_LAYOUT_EPSILON), 1, C_MAX_CONTEXT_EDGE),
            Math.Clamp((int)MathF.Ceiling(logicalWidth * density - C_LAYOUT_EPSILON), 1, C_MAX_CONTEXT_EDGE),
            Math.Clamp((int)MathF.Ceiling(logicalHeight * density - C_LAYOUT_EPSILON), 1, C_MAX_CONTEXT_EDGE),
            density);
        return true;
    }

    private static float ProjectedLength(RenderView view, Matrix localToWorld, Vector3 start, Vector3 end)
    {
        Matrix clip = view.projectionMatrix * view.viewMatrix * localToWorld;
        Vector4 a = Vector4.Transform(new Vector4(start.x, start.y, start.z, 1f), clip);
        Vector4 b = Vector4.Transform(new Vector4(end.x, end.y, end.z, 1f), clip);
        if (MathF.Abs(a.w) < 0.000001f || MathF.Abs(b.w) < 0.000001f)
            return 0f;
        float dx = (b.x / b.w - a.x / a.w) * view.viewport.width * 0.5f;
        float dy = (b.y / b.w - a.y / a.w) * view.viewport.height * 0.5f;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private readonly record struct CanvasLayout(int logicalWidth, int logicalHeight,
        int width, int height, float density);

    private sealed class CanvasState(
        Canvas owner,
        IUiService ui,
        UiContextHandle context,
        UiDocumentHandle document,
        CanvasLayout layout,
        CanvasViewDrawable drawable,
        Guid documentId,
        long documentVersion) : IViewPointerTarget, IDisposable
    {
        internal Canvas owner { get; } = owner;
        internal IUiService ui { get; } = ui;
        internal UiContextHandle context { get; } = context;
        internal UiDocumentHandle document { get; } = document;
        internal CanvasViewDrawable drawable { get; } = drawable;
        internal Guid documentId { get; } = documentId;
        internal long documentVersion { get; } = documentVersion;
        internal CanvasLayout layout { get; set; } = layout;
        internal Matrix planeTransform { get; set; }
        internal ulong frameIndex { get; set; } = ulong.MaxValue;
        private bool m_captured;
        private bool m_keyboardFocused;
        private RenderOutputInput? m_routedInput;
        private Vector2 m_routedPosition;
        private ulong m_routedFrame = ulong.MaxValue;

        public bool hasPointerCapture => m_captured;
        public bool hasKeyboardFocus => m_keyboardFocused;

        public void SetKeyboardFocus(bool focused) => m_keyboardFocused = focused;

        public bool TryHit(RenderView view, RenderOutputInput input, out Vector2 localPosition)
        {
            localPosition = default;
            if (!input.pointerInside && !m_captured)
                return false;
            Matrix localToWorld = planeTransform;
            Vector3 center = Vector3.Transform(Vector3.ZERO, localToWorld);
            Vector3 axisX = Vector3.TransformNormal(Vector3.RIGHT, localToWorld);
            Vector3 axisY = Vector3.TransformNormal(Vector3.UP, localToWorld);
            Vector3 normal = Vector3.Cross(axisX, axisY);
            float xx = Vector3.Dot(axisX, axisX);
            float xy = Vector3.Dot(axisX, axisY);
            float yy = Vector3.Dot(axisY, axisY);
            float determinant = xx * yy - xy * xy;
            if (determinant <= 0.00000001f)
                return false;
            Matrix inverse = Matrix.Invert(view.projectionMatrix * view.viewMatrix);
            float x = input.pointerPosition.x / view.viewport.width * 2f - 1f;
            float y = 1f - input.pointerPosition.y / view.viewport.height * 2f;
            Vector4 near = Vector4.Transform(new Vector4(x, y, -1f, 1f), inverse);
            Vector4 far = Vector4.Transform(new Vector4(x, y, 1f, 1f), inverse);
            if (MathF.Abs(near.w) < 0.000001f || MathF.Abs(far.w) < 0.000001f)
                return false;
            Vector3 rayStart = new(near.x / near.w, near.y / near.w, near.z / near.w);
            Vector3 rayEnd = new(far.x / far.w, far.y / far.w, far.z / far.w);
            Vector3 ray = rayEnd - rayStart;
            float denominator = Vector3.Dot(ray, normal);
            if (MathF.Abs(denominator) < 0.000001f)
                return false;
            float t = Vector3.Dot(center - rayStart, normal) / denominator;
            if ((t < 0f || t > 1f) && !m_captured)
                return false;
            Vector3 point = rayStart + ray * t - center;
            float projectedX = Vector3.Dot(point, axisX);
            float projectedY = Vector3.Dot(point, axisY);
            float localX = (projectedX * yy - projectedY * xy) / determinant;
            float localY = (projectedY * xx - projectedX * xy) / determinant;
            if ((localX < -0.5f || localX > 0.5f || localY < -0.5f || localY > 0.5f)
                && !m_captured)
                return false;
            localPosition = new Vector2((localX + 0.5f) * layout.width,
                (0.5f - localY) * layout.height);
            return m_captured || ui.HasElementAtPoint(context, localPosition);
        }

        public void Advance(RenderOutputInput input, Vector2 localPosition, ulong frameIndex)
        {
            if (!input.interactionEnabled)
            {
                m_captured = false;
                m_keyboardFocused = false;
                localPosition = default;
                input = RenderOutputInput.empty;
            }
            if (m_routedFrame != frameIndex)
            {
                m_routedFrame = frameIndex;
                m_routedInput = null;
            }
            if (HasRoutedInput(input))
                Merge(input, localPosition);
        }

        internal void CompleteFrame(ulong frameIndex)
        {
            if (this.frameIndex == frameIndex)
                return;
            this.frameIndex = frameIndex;
            RenderOutputInput input = m_routedFrame == frameIndex
                ? m_routedInput ?? RenderOutputInput.empty
                : RenderOutputInput.empty;
            Vector2 localPosition = m_routedFrame == frameIndex ? m_routedPosition : default;
            if (input.buttonsPressed.Count > 0)
                m_captured = true;
            Vector2 position = input.pointerInside || m_captured
                ? localPosition : new Vector2(-10000f, -10000f);
            ui.Update(context, new UiInputSnapshot(position, input.scrollDelta, input.modifiers,
                input.keysPressed, input.keysReleased, input.buttonsPressed,
                input.buttonsReleased, input.textInput));
            IReadOnlyList<UiEvent> events = ui.DrainEvents(context);
            drawable.Update(ui.Render(context), layout.width, layout.height,
                planeTransform);
            if (input.buttonsReleased.Count > 0)
                m_captured = false;
            m_routedInput = null;
            owner.PublishEvents(events);
        }

        private void Merge(RenderOutputInput input, Vector2 localPosition)
        {
            if (m_routedInput is RenderOutputInput pending)
            {
                bool useCurrentPointer = input.pointerInside || input.buttonsPressed.Count > 0
                    || input.buttonsReleased.Count > 0;
                m_routedInput = new RenderOutputInput(
                    useCurrentPointer ? input.pointerPosition : pending.pointerPosition,
                    useCurrentPointer ? input.pointerInside : pending.pointerInside,
                    pending.scrollDelta + input.scrollDelta,
                    input.modifiers,
                    pending.keysPressed.Concat(input.keysPressed).Distinct().ToArray(),
                    pending.keysReleased.Concat(input.keysReleased).Distinct().ToArray(),
                    pending.buttonsPressed.Concat(input.buttonsPressed).Distinct().ToArray(),
                    pending.buttonsReleased.Concat(input.buttonsReleased).Distinct().ToArray(),
                    pending.textInput.Concat(input.textInput).ToArray());
                if (useCurrentPointer)
                    m_routedPosition = localPosition;
            }
            else
            {
                m_routedInput = input;
                m_routedPosition = localPosition;
            }
            if (input.buttonsPressed.Count > 0)
                m_captured = true;
        }

        private static bool HasRoutedInput(RenderOutputInput input)
            => input.pointerInside || input.buttonsPressed.Count > 0
                || input.buttonsReleased.Count > 0 || input.keysPressed.Count > 0
                || input.keysReleased.Count > 0 || input.textInput.Count > 0
                || input.scrollDelta.x != 0f || input.scrollDelta.y != 0f;

        public void Dispose()
        {
            drawable.Dispose();
        }
    }
}
