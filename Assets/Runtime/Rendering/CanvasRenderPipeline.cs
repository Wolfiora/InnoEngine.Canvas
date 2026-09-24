using System;
using System.Buffers.Binary;
using System.Collections.Generic;

#if INNO_ENGINE_VALIDATION
using Inno.Core.Diagnostics;
using Inno.Rendering;
using Inno.UI;
#else
using InnoEngine.Diagnostics;
using InnoEngine.Rendering;
using InnoEngine.UI;
#endif

namespace Inno.Canvas;

/// <summary>Consumes UI resource deltas and renders stable meshes from persistent GPU resources.</summary>
[RenderPipelineExtension(CanvasIds.pipeline)]
public sealed class CanvasRenderPipeline : RenderPipeline
{
    private const int C_VERTEX_STRIDE = 20;
    private static readonly RenderBindingId S_TEXTURE_BINDING = new("s_canvasTexture");
    private static readonly RenderPhaseId S_UI_PHASE = new("inno.canvas.ui");
    private static readonly RenderVertexLayout S_VERTEX_LAYOUT = new(
    [
        new(RenderVertexSemantic.Position, RenderVertexFormat.Float2, 0),
        new(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2, 8),
        new(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4, 16)
    ], C_VERTEX_STRIDE);
    private static readonly RenderTextureDescriptor S_WHITE_TEXTURE = new(
        1, 1, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled);
    private static readonly IReadOnlyList<RenderTextureSubresourceData> S_WHITE_TEXTURE_DATA =
    [
        new(0, 0, [byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue])
    ];

    private readonly Dictionary<ResourceKey, MeshState> m_meshes = [];
    private readonly Dictionary<ResourceKey, TextureState> m_textures = [];

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.request.data.TryGet(CanvasRenderFrame.channel, out CanvasRenderFrame? frame) || frame is null)
        {
            context.diagnostics.Publish(new Diagnostic(
                "CANVAS_FRAME_MISSING",
                "The Canvas pipeline request does not contain a compatible UI frame.",
                DiagnosticSeverity.Error,
                CanvasIds.pipeline));
            return;
        }

        ApplyDeltas(context.resourceService, frame);
        if (frame.ui.commands.Count == 0) return;
        context.resourceService.PrewarmMaterial(frame.material);
        if (!context.resourceService.TryResolveGraphicsMaterial(
                frame.material, CanvasIds.materialContract, CanvasIds.premultipliedRole,
                S_VERTEX_LAYOUT, overrides: null, out RenderMaterialPass? material) || material is null)
        {
            context.diagnostics.Publish(new Diagnostic(
                "CANVAS_MATERIAL_UNAVAILABLE",
                "The Canvas material has no ready premultiplied pass for the Canvas vertex contract.",
                DiagnosticSeverity.Warning,
                context.request.name));
            return;
        }

        PreparedCommand[] commands = PrepareCommands(context, frame, material);
        if (commands.Length == 0) return;
        var passData = new PassData(context.request.viewport, material, commands);
        RasterPassBuilder pass = context.graph
            .AddRasterPass("Canvas", S_UI_PHASE, passData, Execute)
            .SetViewTransform(Identity(), Orthographic(context.request.viewport.width, context.request.viewport.height));
        if (context.outputTexture.isValid)
        {
            pass.UseColorAttachment(context.outputTexture, 0, RenderLoadAction.Load, RenderStoreAction.Store);
            context.graph.MarkOutput(context.outputTexture);
        }
        else pass.HasSideEffect();
    }

    private void ApplyDeltas(IRenderResourceService resources, CanvasRenderFrame frame)
    {
        foreach (UiMeshHandle mesh in frame.ui.releasedMeshes)
        {
            var key = new ResourceKey(frame.resourceScope, mesh.value);
            m_meshes.Remove(key);
            resources.Release(VertexId(key));
            resources.Release(IndexId(key));
        }
        foreach (UiTextureHandle texture in frame.ui.releasedTextures)
        {
            var key = new ResourceKey(frame.resourceScope, texture.value);
            m_textures.Remove(key);
            resources.Release(TextureId(key));
        }
        foreach (UiMeshUpdate update in frame.ui.meshUpdates)
        {
            ValidateMesh(update);
            bool index32 = RequiresIndex32(update.indices);
            m_meshes[new(frame.resourceScope, update.mesh.value)] = new(
                update.revision, EncodeVertices(update.vertices), EncodeIndices(update.indices, index32),
                update.vertices.Count, update.indices.Count, index32);
        }
        foreach (UiTextureUpdate update in frame.ui.textureUpdates)
            m_textures[new(frame.resourceScope, update.texture.value)] = new(
                update.revision, update.width, update.height, update.pixels);
    }

    private PreparedCommand[] PrepareCommands(
        RenderPipelineContext context,
        CanvasRenderFrame frame,
        RenderMaterialPass material)
    {
        PersistentTextureHandle white = context.resourceService.AcquireTexture(
            new("inno.canvas.texture.white"), 1, S_WHITE_TEXTURE, S_WHITE_TEXTURE_DATA, "Canvas white texture");
        bool textureUsed = material.UsesBinding(S_TEXTURE_BINDING, RenderShaderBindingKind.Texture);
        var gpuMeshes = new Dictionary<ulong, GpuMesh>();
        var gpuTextures = new Dictionary<ulong, PersistentTextureHandle>();
        var prepared = new List<PreparedCommand>(frame.ui.commands.Count);
        foreach (UiDrawCommand command in frame.ui.commands)
        {
            ResourceKey meshKey = new(frame.resourceScope, command.mesh.value);
            if (!m_meshes.TryGetValue(meshKey, out MeshState? mesh))
                throw new InvalidOperationException($"Canvas draw references unavailable UI mesh {command.mesh.value}.");
            if (mesh.index32 && !context.capabilities.Supports(GraphicsCapability.Index32))
            {
                context.diagnostics.Publish(new Diagnostic(
                    "CANVAS_INDEX32_UNAVAILABLE",
                    "A UI mesh needs 32-bit indices, but the rendering device does not support them.",
                    DiagnosticSeverity.Warning,
                    context.request.name));
                continue;
            }
            if (!gpuMeshes.TryGetValue(command.mesh.value, out GpuMesh gpuMesh))
            {
                gpuMesh = AcquireMesh(context.resourceService, meshKey, mesh);
                gpuMeshes.Add(command.mesh.value, gpuMesh);
            }
            PersistentTextureHandle texture = white;
            if (command.texture.isValid && !gpuTextures.TryGetValue(command.texture.value, out texture))
            {
                ResourceKey textureKey = new(frame.resourceScope, command.texture.value);
                if (!m_textures.TryGetValue(textureKey, out TextureState? state))
                    throw new InvalidOperationException($"Canvas draw references unavailable UI texture {command.texture.value}.");
                texture = context.resourceService.AcquireTexture(
                    TextureId(textureKey), unchecked((long)state.revision),
                    new(state.width, state.height, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled),
                    [new RenderTextureSubresourceData(0, 0, state.pixels.Span)],
                    $"Canvas texture {command.texture.value}");
                gpuTextures.Add(command.texture.value, texture);
            }
            UiClipRectangle clip = Clip(command, context.request.viewport.width, context.request.viewport.height);
            if (clip.width > 0 && clip.height > 0)
                prepared.Add(new(gpuMesh.vertices, gpuMesh.indices, gpuMesh.indexCount, texture, clip, textureUsed));
        }
        return prepared.ToArray();
    }

    private static GpuMesh AcquireMesh(IRenderResourceService resources, ResourceKey key, MeshState mesh)
    {
        PersistentBufferHandle vertices = resources.AcquireBuffer(
            VertexId(key), unchecked((long)mesh.revision),
            new(new RenderBufferDescriptor(mesh.vertexCount, C_VERTEX_STRIDE, RenderBufferUsage.Vertex), S_VERTEX_LAYOUT),
            mesh.vertexBytes, $"Canvas mesh {key.id} vertices");
        int indexStride = mesh.index32 ? sizeof(uint) : sizeof(ushort);
        RenderIndexFormat format = mesh.index32 ? RenderIndexFormat.UInt32 : RenderIndexFormat.UInt16;
        PersistentBufferHandle indices = resources.AcquireBuffer(
            IndexId(key), unchecked((long)mesh.revision),
            new(new RenderBufferDescriptor(mesh.indexCount, indexStride, RenderBufferUsage.Index), indexFormat: format),
            mesh.indexBytes, $"Canvas mesh {key.id} indices");
        return new(vertices, indices, mesh.indexCount);
    }

    private static void Execute(PassData data, RenderPassContext context)
    {
        RenderViewport viewport = data.viewport;
        context.commands.SetViewport(viewport.x, viewport.y, viewport.width, viewport.height);
        data.material.Bind(context.commands);
        foreach (PreparedCommand command in data.commands)
        {
            context.commands.SetScissor(
                checked(viewport.x + command.clip.x), checked(viewport.y + command.clip.y),
                command.clip.width, command.clip.height);
            if (command.textureUsed)
                context.commands.BindTexture(S_TEXTURE_BINDING, command.texture, RenderSamplerState.linearClamp);
            context.commands.BindVertexBuffer(command.vertices);
            context.commands.BindIndexBuffer(command.indices);
            context.commands.DrawIndexed(command.indexCount);
        }
    }

    private static UiClipRectangle Clip(UiDrawCommand command, int width, int height)
    {
        if (!command.scissorEnabled) return new(0, 0, width, height);
        long left = Math.Clamp((long)command.scissor.x, 0, width);
        long top = Math.Clamp((long)command.scissor.y, 0, height);
        long right = Math.Clamp((long)command.scissor.x + command.scissor.width, 0, width);
        long bottom = Math.Clamp((long)command.scissor.y + command.scissor.height, 0, height);
        return new((int)left, (int)top, (int)Math.Max(0, right - left), (int)Math.Max(0, bottom - top));
    }

    private static byte[] EncodeVertices(IReadOnlyList<UiVertex> vertices)
    {
        byte[] bytes = new byte[checked(vertices.Count * C_VERTEX_STRIDE)];
        for (int index = 0; index < vertices.Count; ++index)
        {
            UiVertex vertex = vertices[index];
            Span<byte> target = bytes.AsSpan(index * C_VERTEX_STRIDE, C_VERTEX_STRIDE);
            BinaryPrimitives.WriteSingleLittleEndian(target, vertex.x);
            BinaryPrimitives.WriteSingleLittleEndian(target[4..], vertex.y);
            BinaryPrimitives.WriteSingleLittleEndian(target[8..], vertex.u);
            BinaryPrimitives.WriteSingleLittleEndian(target[12..], vertex.v);
            BinaryPrimitives.WriteUInt32LittleEndian(target[16..], vertex.color);
        }
        return bytes;
    }

    private static byte[] EncodeIndices(IReadOnlyList<uint> indices, bool index32)
    {
        int stride = index32 ? sizeof(uint) : sizeof(ushort);
        byte[] bytes = new byte[checked(indices.Count * stride)];
        for (int index = 0; index < indices.Count; ++index)
        {
            Span<byte> target = bytes.AsSpan(index * stride, stride);
            if (index32) BinaryPrimitives.WriteUInt32LittleEndian(target, indices[index]);
            else BinaryPrimitives.WriteUInt16LittleEndian(target, checked((ushort)indices[index]));
        }
        return bytes;
    }

    private static bool RequiresIndex32(IReadOnlyList<uint> indices)
    {
        foreach (uint index in indices) if (index > ushort.MaxValue) return true;
        return false;
    }

    private static void ValidateMesh(UiMeshUpdate mesh)
    {
        if (mesh.vertices.Count == 0 || mesh.indices.Count == 0)
            throw new InvalidOperationException("Canvas mesh updates require nonempty geometry.");
        foreach (uint index in mesh.indices)
            if (index >= mesh.vertices.Count)
                throw new InvalidOperationException("Canvas mesh index is outside its vertex buffer.");
    }

    private static RenderPersistentResourceId VertexId(ResourceKey key) => new($"{key.scope}.mesh.{key.id}.vertices");
    private static RenderPersistentResourceId IndexId(ResourceKey key) => new($"{key.scope}.mesh.{key.id}.indices");
    private static RenderPersistentResourceId TextureId(ResourceKey key) => new($"{key.scope}.texture.{key.id}");

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f
    ];

    private static float[] Orthographic(int width, int height) =>
    [
        2f / width, 0f, 0f, 0f, 0f, -2f / height, 0f, 0f,
        0f, 0f, 1f, 0f, -1f, 1f, 0f, 1f
    ];

    private readonly record struct ResourceKey(string scope, ulong id);
    private sealed record MeshState(
        ulong revision, byte[] vertexBytes, byte[] indexBytes,
        int vertexCount, int indexCount, bool index32);
    private sealed record TextureState(ulong revision, int width, int height, ReadOnlyMemory<byte> pixels);
    private readonly record struct GpuMesh(PersistentBufferHandle vertices, PersistentBufferHandle indices, int indexCount);
    private sealed record PassData(RenderViewport viewport, RenderMaterialPass material, PreparedCommand[] commands);
    private sealed record PreparedCommand(
        PersistentBufferHandle vertices,
        PersistentBufferHandle indices,
        int indexCount,
        PersistentTextureHandle texture,
        UiClipRectangle clip,
        bool textureUsed);
}
