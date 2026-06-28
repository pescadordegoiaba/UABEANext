namespace UnityComponentPlugin.Logic;

public sealed class EditComponentResult
{
    public string? Name { get; init; }
    public bool? IsActive { get; init; }
    public int? Layer { get; init; }
    public string? TagString { get; init; }

    public string? LocalPosition { get; init; }
    public string? LocalRotation { get; init; }
    public string? LocalScale { get; init; }

    public string? MeshPathId { get; init; }
    public bool? Enabled { get; init; }
    public bool? CastShadows { get; init; }
    public bool? ReceiveShadows { get; init; }

    public bool? IsTrigger { get; init; }
    public bool? Convex { get; init; }
    public string? ColliderSize { get; init; }
    public string? ColliderCenter { get; init; }

    public float? Mass { get; init; }
    public float? Drag { get; init; }
    public float? AngularDrag { get; init; }
    public bool? UseGravity { get; init; }
    public bool? IsKinematic { get; init; }

    public float? FieldOfView { get; init; }
    public float? NearClip { get; init; }
    public float? FarClip { get; init; }
    public bool? Orthographic { get; init; }
    public float? OrthographicSize { get; init; }
}