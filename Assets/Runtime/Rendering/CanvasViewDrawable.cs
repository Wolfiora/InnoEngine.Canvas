using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using InnoEngine.Mathematics;
using InnoEngine.Rendering;
using InnoEngine.UI;

namespace Inno.Canvas;

internal sealed class CanvasViewDrawable : IViewDrawable, IDisposable
{
    private const int C_VERTEX_STRIDE = 24;
    private static readonly RenderBindingId S_TEXTURE_BINDING = new("s_canvasTexture");
    private static readonly RenderVertexLayout S_VERTEX_LAYOUT = new(
    [
        new(RenderVertexSemantic.Position, RenderVertexFormat.Float3, 0),
        new(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2, 12),
        new(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4, 20)
    ], C_VERTEX_STRIDE);
    private static readonly RenderTextureDescriptor S_WHITE_TEXTURE = new(
        1, 1, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled);
    private static readonly IReadOnlyList<RenderTextureSubresourceData> S_WHITE_DATA =
    [
        new(0, 0, [byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue])
    ];

    private readonly string m_scope;
    private readonly MaterialAsset m_material;
    private readonly Dictionary<ulong, UiMeshUpdate> m_meshes = [];
    private readonly Dictionary<ulong, UiTextureUpdate> m_textures = [];
    private readonly Dictionary<int, GeometryState> m_geometry = [];
    private readonly HashSet<RenderPersistentResourceId> m_resourceIds = [];
    private UiRenderFrame m_frame = UiRenderFrame.empty;
    private Matrix m_transform;
    private int m_width;
    private int m_height;
    private IRenderResourceService? m_resources;
    private bool m_disposed;

    internal CanvasViewDrawable(string scope, MaterialAsset material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        m_scope = scope;
        m_material = material ?? throw new ArgumentNullException(nameof(material));
    }

    internal void Update(UiRenderFrame frame, int width, int height, Matrix transform)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        m_frame = frame ?? throw new ArgumentNullException(nameof(frame));
        m_width = width;
        m_height = height;
        m_transform = transform;
        foreach (UiMeshHandle mesh in frame.releasedMeshes)
            m_meshes.Remove(mesh.value);
        foreach (UiMeshUpdate update in frame.meshUpdates)
            m_meshes[update.mesh.value] = update;
        foreach (UiTextureHandle texture in frame.releasedTextures)
        {
            m_textures.Remove(texture.value);
            Release(TextureId(texture.value));
        }
        foreach (UiTextureUpdate update in frame.textureUpdates)
            m_textures[update.texture.value] = update;
        foreach (int index in new List<int>(m_geometry.Keys))
        {
            if (index < frame.commands.Count)
                continue;
            m_geometry.Remove(index);
            Release(VertexId(index));
            Release(IndexId(index));
        }
    }

    public bool TryPrepare(RenderPipelineContext context, RenderView view, out IPreparedViewDrawable? prepared)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        _ = view;
        prepared = null;
        m_resources = context.resourceService;
        context.resourceService.PrewarmMaterial(m_material);
        if (!context.resourceService.TryResolveGraphicsMaterial(
                m_material,
                CanvasIds.materialContract,
                CanvasIds.premultipliedRole,
                S_VERTEX_LAYOUT,
                overrides: null,
                out RenderMaterialPass? material)
            || material is null)
            return false;
        PersistentTextureHandle white = AcquireWhite(context.resourceService);
        bool usesTexture = material.UsesBinding(S_TEXTURE_BINDING, RenderShaderBindingKind.Texture);
        var commands = new List<PreparedCommand>(m_frame.commands.Count);
        for (int index = 0; index < m_frame.commands.Count; index++)
        {
            UiDrawCommand draw = m_frame.commands[index];
            if (!m_meshes.TryGetValue(draw.mesh.value, out UiMeshUpdate? mesh))
                throw new InvalidOperationException($"Canvas references unavailable mesh {draw.mesh.value}.");
            GeometryState geometry = GetGeometry(index, draw, mesh);
            if (geometry.indexCount == 0)
                continue;
            if (geometry.index32 && !context.capabilities.Supports(GraphicsCapability.Index32))
                continue;
            RenderPersistentResourceId vertexId = VertexId(index);
            RenderPersistentResourceId indexId = IndexId(index);
            m_resourceIds.Add(vertexId);
            m_resourceIds.Add(indexId);
            PersistentBufferHandle vertices = context.resourceService.AcquireBuffer(
                vertexId,
                geometry.revision,
                new(new RenderBufferDescriptor(geometry.vertexCount, C_VERTEX_STRIDE, RenderBufferUsage.Vertex),
                    S_VERTEX_LAYOUT),
                geometry.vertices,
                $"Canvas {m_scope} vertices {index}");
            PersistentBufferHandle indices = context.resourceService.AcquireBuffer(
                indexId,
                geometry.revision,
                new(new RenderBufferDescriptor(geometry.indexCount,
                    geometry.index32 ? sizeof(uint) : sizeof(ushort), RenderBufferUsage.Index),
                    indexFormat: geometry.index32 ? RenderIndexFormat.UInt32 : RenderIndexFormat.UInt16),
                geometry.indices,
                $"Canvas {m_scope} indices {index}");
            PersistentTextureHandle texture = white;
            if (draw.texture.isValid)
            {
                if (!m_textures.TryGetValue(draw.texture.value, out UiTextureUpdate? update))
                    throw new InvalidOperationException($"Canvas references unavailable texture {draw.texture.value}.");
                RenderPersistentResourceId textureId = TextureId(draw.texture.value);
                m_resourceIds.Add(textureId);
                texture = context.resourceService.AcquireTexture(
                    textureId,
                    unchecked((long)update.revision),
                    new(update.width, update.height, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled),
                    [new RenderTextureSubresourceData(0, 0, update.pixels.Span)],
                    $"Canvas {m_scope} texture {draw.texture.value}");
            }
            commands.Add(new PreparedCommand(vertices, indices, geometry.indexCount, texture));
        }
        prepared = new PreparedDrawable(material, commands.ToArray(), usesTexture);
        return true;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;
        if (m_resources is not null)
        {
            foreach (RenderPersistentResourceId id in m_resourceIds)
            {
                try { m_resources.Release(id); }
                catch (ObjectDisposedException) { }
            }
        }
        m_resourceIds.Clear();
        m_meshes.Clear();
        m_textures.Clear();
        m_geometry.Clear();
        m_disposed = true;
    }

    private GeometryState GetGeometry(int index, UiDrawCommand draw, UiMeshUpdate mesh)
    {
        UiClipRectangle clip = draw.scissorEnabled
            ? draw.scissor
            : new UiClipRectangle(0, 0, m_width, m_height);
        var key = new GeometryKey(draw.mesh.value, mesh.revision, clip, m_width, m_height, m_transform);
        if (m_geometry.TryGetValue(index, out GeometryState? state) && state.key == key)
            return state;
        var vertices = new List<UiVertex>();
        var indices = new List<uint>();
        float left = Math.Clamp(clip.x, 0, m_width);
        float top = Math.Clamp(clip.y, 0, m_height);
        float right = Math.Clamp((long)clip.x + clip.width, 0, m_width);
        float bottom = Math.Clamp((long)clip.y + clip.height, 0, m_height);
        if (right > left && bottom > top)
        {
            for (int triangle = 0; triangle + 2 < mesh.indices.Count; triangle += 3)
            {
                var polygon = new List<UiVertex>(3)
                {
                    mesh.vertices[checked((int)mesh.indices[triangle])],
                    mesh.vertices[checked((int)mesh.indices[triangle + 1])],
                    mesh.vertices[checked((int)mesh.indices[triangle + 2])]
                };
                polygon = Clip(polygon, static vertex => vertex.x, left, keepGreater: true);
                polygon = Clip(polygon, static vertex => vertex.x, right, keepGreater: false);
                polygon = Clip(polygon, static vertex => vertex.y, top, keepGreater: true);
                polygon = Clip(polygon, static vertex => vertex.y, bottom, keepGreater: false);
                if (polygon.Count < 3)
                    continue;
                uint start = checked((uint)vertices.Count);
                vertices.AddRange(polygon);
                for (uint corner = 1; corner + 1 < polygon.Count; corner++)
                {
                    indices.Add(start);
                    indices.Add(start + corner);
                    indices.Add(start + corner + 1);
                }
            }
        }
        bool index32 = vertices.Count > ushort.MaxValue;
        byte[] vertexBytes = new byte[checked(vertices.Count * C_VERTEX_STRIDE)];
        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            UiVertex vertex = vertices[vertexIndex];
            Vector3 world = Vector3.Transform(new Vector3(
                vertex.x / m_width - 0.5f,
                0.5f - vertex.y / m_height,
                0f), m_transform);
            Span<byte> target = vertexBytes.AsSpan(vertexIndex * C_VERTEX_STRIDE, C_VERTEX_STRIDE);
            BinaryPrimitives.WriteSingleLittleEndian(target, world.x);
            BinaryPrimitives.WriteSingleLittleEndian(target[4..], world.y);
            BinaryPrimitives.WriteSingleLittleEndian(target[8..], world.z);
            BinaryPrimitives.WriteSingleLittleEndian(target[12..], vertex.u);
            BinaryPrimitives.WriteSingleLittleEndian(target[16..], vertex.v);
            BinaryPrimitives.WriteUInt32LittleEndian(target[20..], vertex.color);
        }
        byte[] indexBytes = new byte[indices.Count * (index32 ? sizeof(uint) : sizeof(ushort))];
        for (int valueIndex = 0; valueIndex < indices.Count; valueIndex++)
        {
            Span<byte> target = indexBytes.AsSpan(valueIndex * (index32 ? sizeof(uint) : sizeof(ushort)));
            if (index32)
                BinaryPrimitives.WriteUInt32LittleEndian(target, indices[valueIndex]);
            else
                BinaryPrimitives.WriteUInt16LittleEndian(target, checked((ushort)indices[valueIndex]));
        }
        long revision = unchecked((long)Hash(vertexBytes, indexBytes));
        state = new GeometryState(key, vertexBytes, indexBytes, vertices.Count, indices.Count, index32, revision);
        m_geometry[index] = state;
        return state;
    }

    private static List<UiVertex> Clip(
        IReadOnlyList<UiVertex> polygon,
        Func<UiVertex, float> coordinate,
        float boundary,
        bool keepGreater)
    {
        var result = new List<UiVertex>(polygon.Count + 2);
        if (polygon.Count == 0)
            return result;
        UiVertex previous = polygon[^1];
        float previousCoordinate = coordinate(previous);
        bool previousInside = keepGreater ? previousCoordinate >= boundary : previousCoordinate <= boundary;
        foreach (UiVertex current in polygon)
        {
            float currentCoordinate = coordinate(current);
            bool currentInside = keepGreater ? currentCoordinate >= boundary : currentCoordinate <= boundary;
            if (previousInside != currentInside)
            {
                float t = (boundary - previousCoordinate) / (currentCoordinate - previousCoordinate);
                result.Add(Interpolate(previous, current, t));
            }
            if (currentInside)
                result.Add(current);
            previous = current;
            previousCoordinate = currentCoordinate;
            previousInside = currentInside;
        }
        return result;
    }

    private static UiVertex Interpolate(UiVertex a, UiVertex b, float t)
    {
        uint color = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float channel = ((a.color >> shift) & 0xff) * (1f - t) + ((b.color >> shift) & 0xff) * t;
            color |= (uint)Math.Clamp((int)MathF.Round(channel), 0, 255) << shift;
        }
        return new UiVertex(
            a.x + (b.x - a.x) * t,
            a.y + (b.y - a.y) * t,
            a.u + (b.u - a.u) * t,
            a.v + (b.v - a.v) * t,
            color);
    }

    private PersistentTextureHandle AcquireWhite(IRenderResourceService resources)
    {
        RenderPersistentResourceId id = new($"{m_scope}.white");
        m_resourceIds.Add(id);
        return resources.AcquireTexture(id, 1, S_WHITE_TEXTURE, S_WHITE_DATA, "Canvas white texture");
    }

    private void Release(RenderPersistentResourceId id)
    {
        if (m_resourceIds.Remove(id))
            m_resources?.Release(id);
    }

    private RenderPersistentResourceId VertexId(int index) => new($"{m_scope}.draw.{index}.vertices");
    private RenderPersistentResourceId IndexId(int index) => new($"{m_scope}.draw.{index}.indices");
    private RenderPersistentResourceId TextureId(ulong id) => new($"{m_scope}.texture.{id}");

    private static ulong Hash(ReadOnlySpan<byte> vertices, ReadOnlySpan<byte> indices)
    {
        ulong value = 14695981039346656037UL;
        foreach (byte item in vertices)
            value = unchecked((value ^ item) * 1099511628211UL);
        foreach (byte item in indices)
            value = unchecked((value ^ item) * 1099511628211UL);
        return value;
    }

    private readonly record struct GeometryKey(
        ulong mesh,
        ulong revision,
        UiClipRectangle clip,
        int width,
        int height,
        Matrix transform);

    private sealed record GeometryState(
        GeometryKey key,
        byte[] vertices,
        byte[] indices,
        int vertexCount,
        int indexCount,
        bool index32,
        long revision);

    private readonly record struct PreparedCommand(
        PersistentBufferHandle vertices,
        PersistentBufferHandle indices,
        int indexCount,
        PersistentTextureHandle texture);

    private sealed class PreparedDrawable(
        RenderMaterialPass material,
        PreparedCommand[] draws,
        bool usesTexture) : IPreparedViewDrawable
    {
        public void Encode(RenderCommandEncoder commands)
        {
            material.Bind(commands);
            commands.SetStencil(RenderStencilState.disabled);
            foreach (PreparedCommand draw in draws)
            {
                if (usesTexture)
                    commands.BindTexture(S_TEXTURE_BINDING, draw.texture, RenderSamplerState.linearClamp);
                commands.BindVertexBuffer(draw.vertices);
                commands.BindIndexBuffer(draw.indices);
                commands.DrawIndexed(draw.indexCount);
            }
        }
    }
}
