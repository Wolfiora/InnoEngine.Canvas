using System;
using System.Buffers.Binary;

using InnoEditor.Rendering;
using InnoEditor.Shaders;
using InnoEngine.Rendering;

namespace Inno.Canvas;

/// <summary>Previews the Canvas material contract on an isolated textured UI quad.</summary>
[ShaderPreviewProvider("inno.canvas.material")]
public sealed class CanvasShaderPreview : ShaderPreviewProvider
{
    /// <inheritdoc />
    public override EditorViewportLayer CreateLayer(ShaderPreviewContext context)
    {
        var data = new RenderFrameData();
        data.Set(CanvasShaderPreviewPipeline.channel, context);
        return new("inno.canvas.shader-preview", new RenderPipelineAsset
        { pipelineTypeId = CanvasShaderPreviewPipeline.pipelineId }, data, 0);
    }
}

/// <summary>Draws an isolated compiled Canvas shader without opening a UI context or Scene.</summary>
[RenderPipelineExtension(pipelineId)]
public sealed class CanvasShaderPreviewPipeline : RenderPipeline
{
    /// <summary>Identifies the Editor-only Canvas shader preview pipeline.</summary>
    public const string pipelineId = "inno.canvas.shader-preview";

    /// <summary>Gets the frame-only preview input channel.</summary>
    public static RenderDataChannelId channel => new(pipelineId);

    private static readonly RenderBindingId s_textureBinding = new("s_canvasTexture");
    private static readonly RenderVertexLayout s_vertices = new(
    [
        new(RenderVertexSemantic.Position, RenderVertexFormat.Float3, 0),
        new(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2, 12),
        new(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4, 20)
    ], 24);
    private static readonly byte[] s_vertexBytes = CreateVertices();
    private static readonly byte[] s_indexBytes = [0, 0, 1, 0, 2, 0, 0, 0, 2, 0, 3, 0];
    private static readonly byte[] s_textureBytes = CreateTexture();
    private static readonly float[] s_identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        if (!context.request.data.TryGet(channel, out ShaderPreviewContext? preview) || preview is null)
            throw new InvalidOperationException("Canvas preview input is missing.");
        if (!context.outputTexture.isValid)
            throw new InvalidOperationException("A Canvas preview requires an offscreen output.");
        if (!context.resourceService.TryResolveMaterialArtifact(
                preview.resourceId, preview.artifact, preview.material,
                CanvasIds.materialContract, CanvasIds.premultipliedRole, ShaderProgramKind.Raster,
                s_vertices, null, preview.diagnostics, out RenderMaterialPass? material) || material is null)
            return;

        IRenderResourceService resources = context.resourceService;
        PersistentBufferHandle vertices = resources.AcquireBuffer(new(pipelineId + "/vertices"), 1,
            new(new(4, 24, RenderBufferUsage.Vertex), s_vertices), s_vertexBytes, "Canvas preview vertices");
        PersistentBufferHandle indices = resources.AcquireBuffer(new(pipelineId + "/indices"), 1,
            new(new(6, 2, RenderBufferUsage.Index), indexFormat: RenderIndexFormat.UInt16), s_indexBytes,
            "Canvas preview indices");
        PersistentTextureHandle texture = resources.AcquireTexture(new(pipelineId + "/texture"), 1,
            new(8, 8, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled),
            [new(0, 0, s_textureBytes)], "Canvas preview texture");

        var data = new DrawData(material, vertices, indices, texture, context.request.viewport);
        RasterPassBuilder pass = context.graph.AddRasterPass("Canvas shader preview", new(pipelineId), data,
            static (value, render) => Draw(value, render.commands));
        pass.SetViewTransform(s_identity, s_identity);
        pass.UseColorAttachment(context.outputTexture, 0, RenderLoadAction.Clear, RenderStoreAction.Store,
            new(0.055f, 0.055f, 0.065f, 1f));
        context.graph.MarkOutput(context.outputTexture);
    }

    private static void Draw(DrawData data, RenderCommandEncoder commands)
    {
        commands.SetViewport(0, 0, data.viewport.width, data.viewport.height);
        data.material.Bind(commands);
        if (data.material.UsesBinding(s_textureBinding, RenderShaderBindingKind.Texture))
            commands.BindTexture(s_textureBinding, data.texture, RenderSamplerState.linearClamp);
        commands.BindVertexBuffer(data.vertices);
        commands.BindIndexBuffer(data.indices);
        commands.DrawIndexed(6);
    }

    private static byte[] CreateVertices()
    {
        float[] positionsAndUvs =
        [
            -0.8f, -0.8f, 0f, 0f, 1f,
             0.8f, -0.8f, 0f, 1f, 1f,
             0.8f,  0.8f, 0f, 1f, 0f,
            -0.8f,  0.8f, 0f, 0f, 0f
        ];
        var bytes = new byte[4 * 24];
        for (int vertex = 0; vertex < 4; vertex++)
        {
            for (int component = 0; component < 5; component++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(vertex * 24 + component * 4, 4),
                    positionsAndUvs[vertex * 5 + component]);
            bytes.AsSpan(vertex * 24 + 20, 4).Fill(byte.MaxValue);
        }
        return bytes;
    }

    private static byte[] CreateTexture()
    {
        var pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            int offset = (y * 8 + x) * 4;
            bool border = x is 0 or 7 || y is 0 or 7;
            pixels[offset] = border ? (byte)112 : (byte)77;
            pixels[offset + 1] = border ? (byte)140 : (byte)114;
            pixels[offset + 2] = byte.MaxValue;
            pixels[offset + 3] = byte.MaxValue;
        }
        return pixels;
    }

    private sealed record DrawData(RenderMaterialPass material, PersistentBufferHandle vertices,
        PersistentBufferHandle indices, PersistentTextureHandle texture, RenderViewport viewport);
}
