using System;
using System.Collections.Generic;
using InnoEngine.UI;

namespace Inno.Canvas;

internal sealed class CanvasRenderState
{
    private readonly Dictionary<ulong, UIMeshUpdate> m_meshes = [];
    private readonly Dictionary<ulong, UITextureUpdate> m_textures = [];

    internal IReadOnlyDictionary<ulong, UIMeshUpdate> meshes => m_meshes;
    internal IReadOnlyDictionary<ulong, UITextureUpdate> textures => m_textures;

    internal void Apply(UIRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        foreach (UIMeshHandle mesh in frame.releasedMeshes)
            m_meshes.Remove(mesh.value);
        foreach (UIMeshUpdate update in frame.meshUpdates)
            m_meshes[update.mesh.value] = update;
        foreach (UITextureHandle texture in frame.releasedTextures)
            m_textures.Remove(texture.value);
        foreach (UITextureUpdate update in frame.textureUpdates)
            m_textures[update.texture.value] = update;
    }

    internal void Clear()
    {
        m_meshes.Clear();
        m_textures.Clear();
    }
}
