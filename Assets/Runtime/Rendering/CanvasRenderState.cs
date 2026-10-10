using System;
using System.Collections.Generic;
using InnoEngine.UI;

namespace Inno.Canvas;

internal sealed class CanvasRenderState
{
    private readonly Dictionary<ulong, UiMeshUpdate> m_meshes = [];
    private readonly Dictionary<ulong, UiTextureUpdate> m_textures = [];

    internal IReadOnlyDictionary<ulong, UiMeshUpdate> meshes => m_meshes;
    internal IReadOnlyDictionary<ulong, UiTextureUpdate> textures => m_textures;

    internal void Apply(UiRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        foreach (UiMeshHandle mesh in frame.releasedMeshes)
            m_meshes.Remove(mesh.value);
        foreach (UiMeshUpdate update in frame.meshUpdates)
            m_meshes[update.mesh.value] = update;
        foreach (UiTextureHandle texture in frame.releasedTextures)
            m_textures.Remove(texture.value);
        foreach (UiTextureUpdate update in frame.textureUpdates)
            m_textures[update.texture.value] = update;
    }

    internal void Clear()
    {
        m_meshes.Clear();
        m_textures.Clear();
    }
}
