using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Adapter.Modules.DotNet;
using Inno.Adapter.Serialization.DotNet;
using Inno.Adapter.UI.RmlUi.Authoring;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering.Assets;
using Inno.Scene;
using Inno.UI.Assets;
using Inno.Text.Assets;

namespace Inno.Canvas.Tests;

internal sealed class CanvasFixture : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoCanvasTests", Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly IdentityAllocator m_identities = new();
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly CapturingDiagnosticSink m_diagnosticSink = new();
    private readonly LogRouter m_logs = new();

    internal CanvasFixture()
    {
        Directory.CreateDirectory(assetsPath);
        Assembly[] requiredAssemblies =
        [
            typeof(Canvas).Assembly,
            typeof(CanvasShaderTemplate).Assembly,
            typeof(AssetSerializationServices).Assembly,
            typeof(BgfxShaderSourceFrontend).Assembly,
            typeof(UiDocumentImporter).Assembly,
            typeof(FontImporter).Assembly,
            typeof(SceneAsset).Assembly,
            Assembly.Load("Inno.Scene.Assets"),
            typeof(RmlUiDocumentFrontend).Assembly
        ];
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            catalogSource = new DotNetAssemblyCatalogSource(typeof(CanvasFixture).Assembly),
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        m_modules.Register("CanvasTests", [typeof(Canvas).Assembly]);
        GC.KeepAlive(requiredAssemblies);
        m_types = new TypeCatalog(m_modules, new ReflectionTypeCatalogSource());
        m_serialization = new SerializationRegistry(m_types, new ReflectionSerializationMetadataSource());
        m_diagnostics.RegisterSink(m_diagnosticSink);
        m_types.Rebuild();
        world = new SceneWorld(m_identities, m_types);
    }

    internal string assetsPath => Path.Combine(m_root, "Assets");
    internal SceneWorld world { get; }
    internal TypeCatalog types => m_types;
    internal SerializationRegistry serialization => m_serialization;
    internal string diagnosticSummary => m_diagnosticSink.Summary();

    internal AssetLoader CreateLoader()
        => new(m_types, m_serialization, m_identities, m_diagnostics, m_logs,
            assetsPath, Path.Combine(m_root, "Library"));

    internal IDisposable EnterAssetScope(AssetLoader loader)
        => AssetExecutionContext.EnterScope(new LoaderLookup(loader));

    internal IDisposable EnterLogScope() => m_logs.EnterScope();

    internal void CopyProjectAsset(string relativePath)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "ProjectAssets", relativePath);
        string destination = Path.Combine(assetsPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
        if (File.Exists(source + ".imeta"))
            File.Copy(source + ".imeta", destination + ".imeta");
    }

    public void Dispose()
    {
        world.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_diagnostics.UnregisterSink(m_diagnosticSink);
        m_logs.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    private sealed class CapturingDiagnosticSink : IDiagnosticSink
    {
        private readonly Dictionary<string, DiagnosticReport> m_reports = new(StringComparer.Ordinal);

        public void Replace(DiagnosticReport report) => m_reports[report.source.id] = report;

        public void Clear(DiagnosticSource source) => m_reports.Remove(source.id);

        internal string Summary()
            => string.Join(Environment.NewLine, m_reports.Values
                .SelectMany(static report => report.diagnostics)
                .Select(static diagnostic => $"{diagnostic.severity} {diagnostic.code}: {diagnostic.message}"));
    }

    private sealed class LoaderLookup(AssetLoader loader) : IAssetLookup
    {
        public TAsset Load<TAsset>(AssetPath path) where TAsset : AssetObject
            => loader.Load(path, typeof(TAsset)) as TAsset
                ?? throw new InvalidOperationException($"Asset '{path}' is unavailable as '{typeof(TAsset).Name}'.");

        public TAsset Load<TAsset>(Guid persistentId) where TAsset : AssetObject
            => loader.Load(persistentId, typeof(TAsset)) as TAsset
                ?? throw new InvalidOperationException($"Asset '{persistentId}' is unavailable as '{typeof(TAsset).Name}'.");

        public bool TryLoad<TAsset>(AssetPath path, out TAsset? asset) where TAsset : AssetObject
        {
            asset = loader.Load(path, typeof(TAsset)) as TAsset;
            return asset is not null;
        }

        public bool TryLoad<TAsset>(Guid persistentId, out TAsset? asset) where TAsset : AssetObject
        {
            asset = loader.Load(persistentId, typeof(TAsset)) as TAsset;
            return asset is not null;
        }
    }
}
