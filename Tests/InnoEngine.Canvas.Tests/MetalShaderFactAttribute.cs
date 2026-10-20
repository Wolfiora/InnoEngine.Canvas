using System;
using Xunit;

namespace Inno.Canvas.Tests;

/// <summary>
/// Runs a shader compiler check only where the required native Metal toolchain is available.
/// </summary>
public sealed class MetalShaderFactAttribute : FactAttribute
{
    /// <summary>
    /// Reports the unavailable macOS toolchain as a skipped check during test discovery.
    /// </summary>
    public MetalShaderFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "Requires the native macOS Metal shader toolchain.";
    }
}
