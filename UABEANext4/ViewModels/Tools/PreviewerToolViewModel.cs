using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System;
using System.Linq;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic;
using UABEANext4.Logic.Mesh;
using UABEANext4.Plugins;
using UABEANext4.Util;

namespace UABEANext4.ViewModels.Tools;

public partial class PreviewerToolViewModel : Tool
{
    const string TOOL_TITLE = "Previewer";

    public Workspace Workspace { get; }

    [ObservableProperty]
    public TextDocument? _activeDocument;
    [ObservableProperty]
    public MeshObj? _activeMesh;
    [ObservableProperty]
    public string? _meshPreviewInfo;
    [ObservableProperty]
    public PreviewerToolPreviewType _activePreviewType = PreviewerToolPreviewType.Text;

    [ObservableProperty]
    public ImagePreviewViewModel _imagePreview = new();

    // defer this to first preview since dialogs won't exist until after initial load
    private readonly Lazy<UavPluginFunctions> _uavPluginFuncs = new(() => new UavPluginFunctions());
    private AssetInst? _previewAsset;

    [Obsolete("This constructor is for the designer only and should not be used directly.", true)]
    public PreviewerToolViewModel()
    {
        Workspace = new();

        Id = TOOL_TITLE.Replace(" ", "");
        Title = TOOL_TITLE;

        _activeDocument = new TextDocument();
        _activeMesh = new MeshObj();
    }

    public PreviewerToolViewModel(Workspace workspace)
    {
        Workspace = workspace;

        Id = TOOL_TITLE.Replace(" ", "");
        Title = TOOL_TITLE;

        _activeDocument = new TextDocument("No preview available.");

        WeakReferenceMessenger.Default.Register<AssetsSelectedMessage>(this, OnAssetsSelected);
        WeakReferenceMessenger.Default.Register<AssetsUpdatedMessage>(this, OnAssetsUpdated);
        WeakReferenceMessenger.Default.Register<WorkspaceClosingMessage>(this, OnWorkspaceClosing);
    }

    private void OnAssetsSelected(object recipient, AssetsSelectedMessage message)
    {
        var assets = message.Value;
        VerboseLog.Log("Previewer", $"OnAssetsSelected count={assets.Count}");
        if (assets.Count == 0)
        {
            return;
        }

        var asset = assets[0];
        HandleAssetPreview(asset);
    }

    private void OnAssetsUpdated(object recipient, AssetsUpdatedMessage message)
    {
        if (_previewAsset is null)
        {
            return;
        }

        VerboseLog.Log("Previewer", $"OnAssetsUpdated pathId={message.Value.PathId} previewType={ActivePreviewType}");
        if (ActivePreviewType == PreviewerToolPreviewType.Mesh)
        {
            HandleAssetPreview(_previewAsset);
        }
    }

    private void OnWorkspaceClosing(object recipient, WorkspaceClosingMessage message)
    {
        HandleAssetPreview(null);
    }

    private void HandleAssetPreview(AssetInst? asset)
    {
        _previewAsset = asset;

        if (asset is null)
        {
            VerboseLog.Log("Previewer", "HandleAssetPreview cleared");
            MeshPreviewInfo = null;
            ActiveMesh = null;
            SetDisplayText(string.Empty);
            return;
        }

        try
        {
            HandleAssetPreviewCore(asset);
        }
        catch (Exception ex)
        {
            VerboseLog.LogException("Previewer", ex, $"HandleAssetPreview failed for {asset.DisplayName}");
            SetDisplayText($"Preview failed: {ex.Message}");
        }
    }

    private void HandleAssetPreviewCore(AssetInst asset)
    {
        using var scope = VerboseLog.Scope("Previewer", "HandleAssetPreview", $"{asset.DisplayName} pathId={asset.PathId}");
        var pluginsList = Workspace.Plugins.GetPreviewersThatSupport(Workspace, asset);
        if (pluginsList == null || pluginsList.Count == 0)
        {
            VerboseLog.Log("Previewer", "No previewer plugin supports this asset");
            SetDisplayText("No preview available.");
            return;
        }

        static int PreviewPriority(UavPluginPreviewerType t) => t switch
        {
            UavPluginPreviewerType.Mesh => 0,
            UavPluginPreviewerType.Image => 1,
            UavPluginPreviewerType.Text => 2,
            _ => 3
        };

        var ordered = pluginsList
            .OrderBy(p => PreviewPriority(p.PreviewType))
            .ToList();

        var firstPrevPair = ordered[0];
        VerboseLog.Log("Previewer", $"Using previewer {firstPrevPair.Previewer.GetType().Name} type={firstPrevPair.PreviewType}");
        var prevType = firstPrevPair.PreviewType;
        var prev = firstPrevPair.Previewer;

        switch (prevType)
        {
            case UavPluginPreviewerType.Image:
            {
                ActivePreviewType = PreviewerToolPreviewType.Image;

                var (image, format) = prev.ExecuteImage(Workspace, _uavPluginFuncs.Value, asset, out string? error);
                if (image != null)
                {
                    ImagePreview.UpdateImage(image, (AssetsTools.NET.Texture.TextureFormat?)format);
                }
                else
                {
                    SetDisplayText(error ?? "[null error]");
                }
                break;
            }
            case UavPluginPreviewerType.Text:
            {
                ActivePreviewType = PreviewerToolPreviewType.Text;

                var textString = prev.ExecuteText(Workspace, _uavPluginFuncs.Value, asset, out string? error);
                if (textString != null)
                {
                    ActiveDocument = new TextDocument(textString);
                }
                else
                {
                    SetDisplayText(error ?? "[null error]");
                }
                break;
            }
            case UavPluginPreviewerType.Mesh:
            {
                ActivePreviewType = PreviewerToolPreviewType.Mesh;

                var meshObj = prev.ExecuteMesh(Workspace, _uavPluginFuncs.Value, asset, out string? error);
                if (meshObj != null)
                {
                    ActiveMesh = null;
                    ActiveMesh = meshObj;
                    MeshPreviewInfo =
                        $"Vertices: {meshObj.VertexCount}  Triangles: {meshObj.Indices.Length / 3}\n" +
                        "LMB: rotate  RMB: pan  Wheel: zoom  |  Ctrl+W: wire  Ctrl+S: shade  Ctrl+N: normals  R: reset";
                }
                else
                {
                    MeshPreviewInfo = null;
                    var textFallback = ordered.FirstOrDefault(p => p.PreviewType == UavPluginPreviewerType.Text);
                    if (textFallback is not null)
                    {
                        var text = textFallback.Previewer.ExecuteText(Workspace, _uavPluginFuncs.Value, asset, out error);
                        if (text != null)
                        {
                            VerboseLog.Log("Previewer", "Mesh preview failed; showing text component summary");
                            SetDisplayText(text);
                            break;
                        }
                    }
                    SetDisplayText(error ?? "[null error]");
                }
                break;
            }
            default:
            {
                SetDisplayText($"Preview type {prevType} not supported.");
                break;
            }
        }
    }

    private void SetDisplayText(string text)
    {
        ActivePreviewType = PreviewerToolPreviewType.Text;
        ActiveDocument = new TextDocument(text);
    }
}

public enum PreviewerToolPreviewType
{
    Image,
    Text,
    Mesh,
}
