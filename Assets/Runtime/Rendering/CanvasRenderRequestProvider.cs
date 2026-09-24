using System;
using System.Collections.Generic;

#if INNO_ENGINE_VALIDATION
using Inno.References;
using Inno.Rendering;
using Inno.Scene;
using Inno.UI;
using UiApi = Inno.UI.UI;
#else
using InnoEngine.References;
using InnoEngine.Rendering;
using InnoEngine.Scene;
using InnoEngine.UI;
using UiApi = InnoEngine.UI.UI;
#endif

namespace Inno.Canvas;

/// <summary>
/// Owns Canvas UI contexts and submits their immutable geometry to the Canvas pipeline.
/// </summary>
[RenderRequestProviderExtension(CanvasIds.requestProvider)]
public sealed class CanvasRenderRequestProvider : RenderRequestProvider
{
    private readonly Dictionary<Guid, CanvasState> m_states = [];

    /// <inheritdoc />
    public override void Submit(RenderRequestProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var active = new HashSet<Guid>();
        foreach (GameScene scene in context.content.GetValues<GameScene>())
        {
            if (scene.isDestroyed)
                continue;
            foreach (GameObject gameObject in scene.GetObjects())
            {
                if (!gameObject.TryGetComponent(out Canvas? canvas)
                    || canvas is null
                    || !canvas.isActiveAndEnabled
                    || !CanRender(canvas))
                {
                    continue;
                }

                Guid id = canvas.identity.persistentId;
                active.Add(id);
                CanvasState state = GetOrCreate(canvas, context);
                RenderCanvas(state, context);
            }
        }

        foreach (Guid id in new List<Guid>(m_states.Keys))
        {
            if (active.Contains(id))
                continue;
            Retire(m_states[id], context);
            m_states.Remove(id);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        foreach (Guid id in new List<Guid>(m_states.Keys))
        {
            CanvasState state = m_states[id];
            try { state.ui.DestroyContext(state.context); }
            catch (ObjectDisposedException) { }
            state.owner.Unbind(state.context);
            m_states.Remove(id);
        }
    }

    private static bool CanRender(Canvas canvas)
        => canvas.document is { isMissing: false, source: not null }
            && canvas.font is null or { isMissing: false }
            && canvas.pipeline is { isMissing: false, pipelineTypeId: CanvasIds.pipeline }
            && canvas.material is { isMissing: false, shader: { isMissing: false } };

    private CanvasState GetOrCreate(Canvas canvas, RenderRequestProviderContext context)
    {
        Guid id = canvas.identity.persistentId;
        CanvasDescriptor descriptor = CanvasDescriptor.Capture(canvas, context.primaryPresentationSize);
        if (m_states.TryGetValue(id, out CanvasState? current))
        {
            if (ReferenceEquals(current.owner, canvas)
                && ReferenceEquals(current.pipeline, canvas.pipeline)
                && ReferenceEquals(current.material, canvas.material)
                && current.descriptor == descriptor)
                return current;
            Retire(current, context);
            m_states.Remove(id);
        }

        IUiService ui = UiExecutionContext.current;
        UiContextHandle uiContext = ui.CreateContext(new UiContextOptions(
            $"Canvas/{id:D}",
            descriptor.width,
            descriptor.height,
            descriptor.density));
        try
        {
            if (canvas.font is not null)
            {
                UiApi.RegisterFont(
                    canvas.font,
                    canvas.fontFamily,
                    style: canvas.fontStyle,
                    weight: canvas.fontWeight,
                    fallback: canvas.fontFallback);
            }
            UiDocumentHandle document = UiApi.LoadDocument(uiContext, canvas.document!);
            UiApi.ShowDocument(uiContext, document);
            var created = new CanvasState(
                canvas,
                gameObjectName: canvas.gameObject.name,
                canvas.pipeline!,
                canvas.material!,
                ui,
                descriptor,
                uiContext,
                document);
            canvas.Bind(uiContext, document);
            m_states.Add(id, created);
            return created;
        }
        catch
        {
            ui.DestroyContext(uiContext);
            throw;
        }
    }

    private static void RenderCanvas(CanvasState state, RenderRequestProviderContext context)
    {
        UiApi.Update(state.context);
        state.owner.PublishEvents(UiApi.DrainEvents(state.context));
        UiRenderFrame ui = UiApi.Render(state.context);
        state.Track(ui);
        var frame = new CanvasRenderFrame(ui, state.material, state.resourceScope);
        context.requests.Submit(new RenderRequest(
            $"Canvas/{state.gameObjectName}",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, state.descriptor.width, state.descriptor.height),
            state.pipeline,
            frame.ToRenderFrameData(),
            SaturatingPriority(state.owner.order)));
    }

    private static void Retire(CanvasState state, RenderRequestProviderContext context)
    {
        state.owner.Unbind(state.context);
        if (state.activeTextures.Count != 0 || state.activeMeshes.Count != 0)
        {
            UiRenderFrame released = UiRenderFrame.CreateRetirement(
                [.. state.activeMeshes],
                [.. state.activeTextures]);
            var frame = new CanvasRenderFrame(released, state.material, state.resourceScope);
            context.requests.Submit(new RenderRequest(
                $"Canvas/{state.gameObjectName}/Retire",
                RenderTarget.backbuffer,
                new RenderViewport(0, 0, state.descriptor.width, state.descriptor.height),
                state.pipeline,
                frame.ToRenderFrameData(),
                SaturatingPriority(state.owner.order)));
        }
        state.ui.DestroyContext(state.context);
    }

    private static int SaturatingPriority(int order)
    {
        long combined = (long)CanvasIds.presentationOrder + order;
        return (int)Math.Clamp(combined, int.MinValue, int.MaxValue);
    }

    private readonly record struct CanvasDescriptor(
        Guid documentId,
        long documentVersion,
        Guid fontId,
        long fontVersion,
        string fontFamily,
        int fontStyle,
        int fontWeight,
        bool fontFallback,
        int width,
        int height,
        float density)
    {
        internal static CanvasDescriptor Capture(Canvas canvas, RenderPresentationSize presentation)
            => new(
                canvas.document!.identity.persistentId,
                canvas.document.contentVersion,
                canvas.font?.identity.persistentId ?? Guid.Empty,
                canvas.font?.contentVersion ?? 0,
                canvas.fontFamily,
                (int)canvas.fontStyle,
                canvas.fontWeight,
                canvas.fontFallback,
                presentation.width,
                presentation.height,
                canvas.density);
    }

    private sealed class CanvasState(
        Canvas owner,
        string gameObjectName,
        RenderPipelineAsset pipeline,
        MaterialAsset material,
        IUiService ui,
        CanvasDescriptor descriptor,
        UiContextHandle context,
        UiDocumentHandle document)
    {
        internal readonly HashSet<UiMeshHandle> activeMeshes = [];
        internal readonly HashSet<UiTextureHandle> activeTextures = [];
        internal readonly string resourceScope = $"inno.canvas.context.{context.value}";
        internal Canvas owner { get; } = owner;
        internal string gameObjectName { get; } = gameObjectName;
        internal RenderPipelineAsset pipeline { get; } = pipeline;
        internal MaterialAsset material { get; } = material;
        internal IUiService ui { get; } = ui;
        internal CanvasDescriptor descriptor { get; } = descriptor;
        internal UiContextHandle context { get; } = context;
        internal UiDocumentHandle document { get; } = document;

        internal void Track(UiRenderFrame frame)
        {
            foreach (UiMeshHandle released in frame.releasedMeshes)
                activeMeshes.Remove(released);
            foreach (UiMeshUpdate mesh in frame.meshUpdates)
                activeMeshes.Add(mesh.mesh);
            foreach (UiTextureHandle released in frame.releasedTextures)
                activeTextures.Remove(released);
            foreach (UiTextureUpdate texture in frame.textureUpdates)
                activeTextures.Add(texture.texture);
        }
    }
}
