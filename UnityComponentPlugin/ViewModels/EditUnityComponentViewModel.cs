using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Numerics;
using UABEANext4.AssetWorkspace;
using UABEANext4.Interfaces;
using UABEANext4.ViewModels;
using UnityComponentPlugin.Logic;

namespace UnityComponentPlugin.ViewModels;

public partial class EditUnityComponentViewModel : ViewModelBase, IDialogAware<EditComponentResult?>
{
    [ObservableProperty] private ComponentEditKind _kind;
    [ObservableProperty] private string _assetSummary = "";

    [ObservableProperty] private string? _name;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string? _layerString;
    [ObservableProperty] private string? _tagString;

    [ObservableProperty] private string? _localPosition;
    [ObservableProperty] private string? _localRotationEuler;
    [ObservableProperty] private string? _localScale;

    [ObservableProperty] private string? _meshPathId;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool _castShadows = true;
    [ObservableProperty] private bool _receiveShadows = true;

    [ObservableProperty] private bool _isTrigger;
    [ObservableProperty] private bool _convex;
    [ObservableProperty] private string? _colliderSize;
    [ObservableProperty] private string? _colliderCenter;

    [ObservableProperty] private string? _massString;
    [ObservableProperty] private string? _dragString;
    [ObservableProperty] private string? _angularDragString;
    [ObservableProperty] private bool _useGravity = true;
    [ObservableProperty] private bool _isKinematic;

    [ObservableProperty] private string? _fieldOfViewString;
    [ObservableProperty] private string? _nearClipString;
    [ObservableProperty] private string? _farClipString;
    [ObservableProperty] private bool _orthographic;
    [ObservableProperty] private string? _orthographicSizeString;

    public bool ShowGameObject => Kind == ComponentEditKind.GameObject;
    public bool ShowTransform => Kind == ComponentEditKind.Transform;
    public bool ShowMeshLink => Kind is ComponentEditKind.MeshFilter or ComponentEditKind.MeshCollider;
    public bool ShowRenderer => Kind == ComponentEditKind.MeshRenderer;
    public bool ShowShader => Kind == ComponentEditKind.Shader;
    public bool ShowBoxCollider => Kind == ComponentEditKind.BoxCollider;
    public bool ShowMeshCollider => Kind == ComponentEditKind.MeshCollider;
    public bool ShowRigidbody => Kind == ComponentEditKind.Rigidbody;
    public bool ShowCamera => Kind == ComponentEditKind.Camera;

    public string Title => "Edit Unity Component";
    public int Width => 480;
    public int Height => 520;
    public bool IsModal => true;
    public event Action<EditComponentResult?>? RequestClose;

    public EditUnityComponentViewModel(Workspace workspace, AssetInst asset)
    {
        var kind = ComponentEditKindExtensions.FromAssetType(asset.Type)
            ?? throw new InvalidOperationException($"Unsupported type {asset.Type}");
        Kind = kind;
        AssetSummary = $"{asset.Type} — {asset.DisplayName} (pathId {asset.PathId})";

        var bf = workspace.GetBaseField(asset);
        if (bf is null)
            return;

        LoadFields(asset, bf);
    }

    partial void OnKindChanged(ComponentEditKind value)
    {
        OnPropertyChanged(nameof(ShowGameObject));
        OnPropertyChanged(nameof(ShowTransform));
        OnPropertyChanged(nameof(ShowMeshLink));
        OnPropertyChanged(nameof(ShowRenderer));
        OnPropertyChanged(nameof(ShowShader));
        OnPropertyChanged(nameof(ShowBoxCollider));
        OnPropertyChanged(nameof(ShowMeshCollider));
        OnPropertyChanged(nameof(ShowRigidbody));
        OnPropertyChanged(nameof(ShowCamera));
    }

    private void LoadFields(AssetInst asset, AssetsTools.NET.AssetTypeValueField bf)
    {
        switch (Kind)
        {
            case ComponentEditKind.GameObject:
                if (ComponentFieldHelper.TryGetString(bf, "m_Name", out var goName))
                    Name = goName;
                if (ComponentFieldHelper.TryGetBool(bf, "m_IsActive", out var active))
                    IsActive = active;
                if (ComponentFieldHelper.TryGetInt(bf, "m_Layer", out var layer))
                    LayerString = layer.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetString(bf, "m_TagString", out var tag))
                    TagString = tag;
                break;

            case ComponentEditKind.Transform:
                if (ComponentFieldHelper.TryGetVector3(bf, "m_LocalPosition", out var pos))
                    LocalPosition = ComponentFieldHelper.FormatVector3(pos);
                if (ComponentFieldHelper.TryGetVector3(bf, "m_LocalScale", out var scale))
                    LocalScale = ComponentFieldHelper.FormatVector3(scale);
                if (ComponentFieldHelper.TryGetQuaternion(bf, "m_LocalRotation", out var rot))
                    LocalRotationEuler = QuaternionToEulerDegrees(rot);
                break;

            case ComponentEditKind.MeshFilter:
            case ComponentEditKind.MeshCollider:
                var meshPtr = bf["m_Mesh"];
                if (!meshPtr.IsDummy && !meshPtr["m_PathID"].IsDummy)
                    MeshPathId = meshPtr["m_PathID"].AsLong.ToString(CultureInfo.InvariantCulture);
                if (Kind == ComponentEditKind.MeshCollider)
                {
                    if (ComponentFieldHelper.TryGetBool(bf, "m_IsTrigger", out var trigger))
                        IsTrigger = trigger;
                    if (ComponentFieldHelper.TryGetBool(bf, "m_Convex", out var convex))
                        Convex = convex;
                    if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var en))
                        Enabled = en;
                }
                break;

            case ComponentEditKind.MeshRenderer:
                if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var enabled))
                    Enabled = enabled;
                if (ComponentFieldHelper.TryGetBool(bf, "m_CastShadows", out var cast))
                    CastShadows = cast;
                if (ComponentFieldHelper.TryGetBool(bf, "m_ReceiveShadows", out var recv))
                    ReceiveShadows = recv;
                break;

            case ComponentEditKind.Shader:
                if (ComponentFieldHelper.TryGetString(bf, "m_Name", out var shaderName))
                    Name = shaderName;
                break;

            case ComponentEditKind.BoxCollider:
                if (ComponentFieldHelper.TryGetBool(bf, "m_IsTrigger", out var bTrigger))
                    IsTrigger = bTrigger;
                if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var bEn))
                    Enabled = bEn;
                if (ComponentFieldHelper.TryGetVector3(bf, "m_Size", out var size))
                    ColliderSize = ComponentFieldHelper.FormatVector3(size);
                if (ComponentFieldHelper.TryGetVector3(bf, "m_Center", out var center))
                    ColliderCenter = ComponentFieldHelper.FormatVector3(center);
                break;

            case ComponentEditKind.Rigidbody:
                if (ComponentFieldHelper.TryGetFloat(bf, "m_Mass", out var mass))
                    MassString = mass.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetFloat(bf, "m_Drag", out var drag))
                    DragString = drag.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetFloat(bf, "m_AngularDrag", out var ang))
                    AngularDragString = ang.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetBool(bf, "m_UseGravity", out var grav))
                    UseGravity = grav;
                if (ComponentFieldHelper.TryGetBool(bf, "m_IsKinematic", out var kin))
                    IsKinematic = kin;
                break;

            case ComponentEditKind.Camera:
                if (ComponentFieldHelper.TryGetBool(bf, "m_Enabled", out var camEn))
                    Enabled = camEn;
                if (ComponentFieldHelper.TryGetFloat(bf, "m_FieldOfView", out var fov))
                    FieldOfViewString = fov.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetFloat(bf, "m_NearClipPlane", out var near))
                    NearClipString = near.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetFloat(bf, "m_FarClipPlane", out var far))
                    FarClipString = far.ToString(CultureInfo.InvariantCulture);
                if (ComponentFieldHelper.TryGetBool(bf, "m_Orthographic", out var ortho))
                    Orthographic = ortho;
                if (ComponentFieldHelper.TryGetFloat(bf, "m_OrthographicSize", out var orthoSize))
                    OrthographicSizeString = orthoSize.ToString(CultureInfo.InvariantCulture);
                break;
        }
    }

    private static string QuaternionToEulerDegrees(Quaternion q)
    {
        var yaw = MathF.Atan2(2f * (q.Y * q.W + q.X * q.Z), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
        var pitch = MathF.Asin(MathF.Max(-1f, MathF.Min(1f, 2f * (q.X * q.W - q.Y * q.Z))));
        var roll = MathF.Atan2(2f * (q.X * q.W + q.Y * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        var x = pitch * 180f / MathF.PI;
        var y = yaw * 180f / MathF.PI;
        var z = roll * 180f / MathF.PI;
        return $"{x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}, {z.ToString(CultureInfo.InvariantCulture)}";
    }

    [RelayCommand]
    private void Save()
    {
        int? layer = null;
        if (LayerString is not null && int.TryParse(LayerString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layerVal))
            layer = layerVal;

        float? mass = ParseFloat(MassString);
        float? drag = ParseFloat(DragString);
        float? angDrag = ParseFloat(AngularDragString);
        float? fov = ParseFloat(FieldOfViewString);
        float? near = ParseFloat(NearClipString);
        float? far = ParseFloat(FarClipString);
        float? orthoSize = ParseFloat(OrthographicSizeString);

        RequestClose?.Invoke(new EditComponentResult
        {
            Name = Name,
            IsActive = Kind == ComponentEditKind.GameObject ? IsActive : null,
            Layer = layer,
            TagString = TagString,
            LocalPosition = LocalPosition,
            LocalRotation = LocalRotationEuler,
            LocalScale = LocalScale,
            MeshPathId = MeshPathId,
            Enabled = ShowRenderer || ShowBoxCollider || ShowMeshCollider || ShowCamera ? Enabled : null,
            CastShadows = ShowRenderer ? CastShadows : null,
            ReceiveShadows = ShowRenderer ? ReceiveShadows : null,
            IsTrigger = ShowBoxCollider || ShowMeshCollider ? IsTrigger : null,
            Convex = ShowMeshCollider ? Convex : null,
            ColliderSize = ColliderSize,
            ColliderCenter = ColliderCenter,
            Mass = mass,
            Drag = drag,
            AngularDrag = angDrag,
            UseGravity = ShowRigidbody ? UseGravity : null,
            IsKinematic = ShowRigidbody ? IsKinematic : null,
            FieldOfView = fov,
            NearClip = near,
            FarClip = far,
            Orthographic = ShowCamera ? Orthographic : null,
            OrthographicSize = orthoSize,
        });
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(null);

    private static float? ParseFloat(string? text) =>
        text is not null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}