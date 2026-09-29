using System;
using InnoEditor.Rendering;
using InnoEngine.Mathematics;
using InnoEngine.Scene;
using InnoEngine.Settings;

namespace Inno.Canvas;

/// <summary>
/// Contributes selectable world Canvas icons and selected virtual-resolution bounds.
/// </summary>
[EditorGizmoProviderExtension("inno.canvas.scene-gizmos")]
public sealed class CanvasGizmoProvider : EditorGizmoProvider
{
    /// <inheritdoc />
    public override void Collect(
        EditorGizmoContext context,
        IEditorGizmoSink sink
    ) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sink);
        float pixelsPerUnit = Settings.Get<CanvasProjectSettings>(CanvasProjectSettings.id)
            .logicalPixelsPerWorldUnit;
        foreach (GameScene scene in context.content.GetValues<GameScene>())
        {
            if (scene.isDestroyed)
                continue;
            foreach (GameObject owner in scene.GetObjects())
            {
                if (!owner.activeInHierarchy ||
                    !owner.TryGetComponent(out Canvas? canvas) ||
                    canvas is not { isActiveAndEnabled: true })
                    continue;
                sink.Icon(owner.identity, owner.transform.worldPosition, "canvas");
                if (owner.identity.runtimeIdentity != context.selected)
                    continue;
                float halfWidth = canvas.referenceWidth / pixelsPerUnit * 0.5f;
                float halfHeight = canvas.referenceHeight / pixelsPerUnit * 0.5f;
                Vector3[] corners =
                [
                    owner.transform.TransformPoint(new Vector3(-halfWidth, -halfHeight, 0f)),
                    owner.transform.TransformPoint(new Vector3(halfWidth, -halfHeight, 0f)),
                    owner.transform.TransformPoint(new Vector3(halfWidth, halfHeight, 0f)),
                    owner.transform.TransformPoint(new Vector3(-halfWidth, halfHeight, 0f))
                ];
                for (int index = 0; index < corners.Length; index++)
                    sink.Line(corners[index], corners[(index + 1) % corners.Length]);
            }
        }
    }
}
