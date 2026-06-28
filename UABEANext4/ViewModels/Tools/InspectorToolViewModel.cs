using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.ObjectModel;
using UABEANext4.AssetWorkspace;
using UABEANext4.Logic;
using UABEANext4.Util;

namespace UABEANext4.ViewModels.Tools;
public partial class InspectorToolViewModel : Tool
{
    const string TOOL_TITLE = "Inspector";

    public Workspace Workspace { get; }

    [ObservableProperty]
    public ObservableCollection<AssetInst> _activeAssets;

    [Obsolete("This constructor is for the designer only and should not be used directly.", true)]
    public InspectorToolViewModel()
    {
        Workspace = new();
        ActiveAssets = new();

        Id = TOOL_TITLE.Replace(" ", "");
        Title = TOOL_TITLE;
    }

    public InspectorToolViewModel(Workspace workspace)
    {
        Workspace = workspace;
        ActiveAssets = new();

        Id = TOOL_TITLE.Replace(" ", "");
        Title = TOOL_TITLE;

        WeakReferenceMessenger.Default.Register<AssetsSelectedMessage>(this, OnAssetsSelected);
        WeakReferenceMessenger.Default.Register<AssetsUpdatedMessage>(this, OnAssetsUpdated);
        WeakReferenceMessenger.Default.Register<WorkspaceClosingMessage>(this, OnWorkspaceClosing);
    }

    private void OnAssetsSelected(object recipient, AssetsSelectedMessage message)
    {
        var assets = message.Value;
        VerboseLog.Log("Inspector", $"OnAssetsSelected count={assets.Count}" + (assets.Count > 0 ? $" first={assets[0].DisplayName} pathId={assets[0].PathId}" : ""));
        if (assets.Count == ActiveAssets.Count)
        {
            var sameSelection = true;
            for (int i = 0; i < assets.Count; i++)
            {
                if (!ReferenceEquals(assets[i], ActiveAssets[i]))
                {
                    sameSelection = false;
                    break;
                }
            }

            if (sameSelection)
            {
                VerboseLog.Log("Inspector", "OnAssetsSelected skipped (same selection)");
                return;
            }
        }

        ActiveAssets.Clear();
        foreach (var asset in assets)
        {
            ActiveAssets.Add(asset);
        }
    }

    private void OnAssetsUpdated(object recipient, AssetsUpdatedMessage message)
    {
        var asset = message.Value;
        var index = ActiveAssets.IndexOf(asset);
        if (index != -1)
        {
            ActiveAssets[index] = asset;
        }
    }

    private void OnWorkspaceClosing(object recipient, WorkspaceClosingMessage message)
    {
        ActiveAssets.Clear();
    }
}
