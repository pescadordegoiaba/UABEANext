using AssetsTools.NET.Extra;
using System.Text;
using UABEANext4.AssetWorkspace;
using UABEANext4.Plugins;
using UnityComponentPlugin.Logic;
using UnityComponentPlugin.ViewModels;

namespace UnityComponentPlugin;

public class EditUnityComponentOption : IUavPluginOption
{
    public string Name => "Edit Unity Component";
    public string Description => "Edit GameObject, Transform, mesh components, colliders, Rigidbody, Camera, and Shader fields";
    public UavPluginMode Options => UavPluginMode.Export;

    public bool SupportsSelection(Workspace workspace, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (mode != UavPluginMode.Export || selection.Count == 0)
            return false;

        var firstKind = ComponentEditKindExtensions.FromAssetType(selection[0].Type);
        if (firstKind is null)
            return false;

        return selection.All(a => ComponentEditKindExtensions.FromAssetType(a.Type) == firstKind);
    }

    public async Task<bool> Execute(Workspace workspace, IUavPluginFunctions funcs, UavPluginMode mode, IList<AssetInst> selection)
    {
        if (selection.Count == 0)
            return false;

        var editVm = new EditUnityComponentViewModel(workspace, selection[0]);
        var result = await funcs.ShowDialog(editVm);
        if (result is null)
            return false;

        var errors = new StringBuilder();
        foreach (var asset in selection)
        {
            var bf = workspace.GetBaseField(asset);
            if (bf is null)
            {
                errors.AppendLine($"[{asset.DisplayName}]: could not load fields");
                continue;
            }

            try
            {
                if (!ComponentEditApplier.Apply(workspace, asset, bf, result))
                    errors.AppendLine($"[{asset.DisplayName}]: type not supported for edits");
                else
                    asset.UpdateAssetDataAndRow(workspace, bf);
            }
            catch (Exception ex)
            {
                errors.AppendLine($"[{asset.DisplayName}]: {ex.Message}");
            }
        }

        if (errors.Length > 0)
            await funcs.ShowMessageDialog("Edit Unity Component", errors.ToString());

        return true;
    }
}