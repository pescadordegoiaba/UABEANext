using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using UABEANext4.Logic.Mesh;

namespace UABEANext4.Controls.MeshPreviewer;

public class MeshPreviewerControl : OpenGlControlBase, ICustomHitTest
{
    private GL? _gl;
    private bool _loaded;
    private bool _dirtyModel;

    private uint _shaderProgram;
    private uint _vertexBufferObject;
    private uint _indexBufferObject;
    private uint _wireBufferObject;
    private uint _vertexArrayObject;

    private MeshPreviewRenderData? _renderData;
    private GpuVertex[] _gpuVertices = [];
    private uint[] _triangleIndices = [];
    private uint[] _wireframeIndices = [];

    private Vector3 _cameraPos = new(0f, 0f, 2.5f);
    private Vector2 _orbit = new(-MathF.PI / 4, -MathF.PI / 6);
    private Vector2 _pan = Vector2.Zero;
    private float _cameraDistance = 2.5f;
    private Vector2 _lastPos = new(-1f, -1f);
    private bool _leftDown;
    private bool _rightDown;
    private Matrix4x4 _modelMatrix = Matrix4x4.Identity;

    private int _wireFrameMode;
    private int _shadeMode;
    private int _normalMode;
    private bool _useAssetNormals = true;

    const float PIH_MINUS_EPSILON = (MathF.PI / 2) - 0.0001f;

    public MeshPreviewerControl()
    {
        Focusable = true;
        ActiveMeshProperty.Changed.Subscribe(ActiveMesh_Changed);

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerMoved += OnPointerMoved;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;

        RecalculateCamera();
    }

    public static readonly DirectProperty<MeshPreviewerControl, MeshObj?> ActiveMeshProperty =
        AvaloniaProperty.RegisterDirect<MeshPreviewerControl, MeshObj?>(
            nameof(ActiveMesh), o => o.ActiveMesh, (o, v) => o.ActiveMesh = v);

    private MeshObj? _activeMesh;
    public MeshObj? ActiveMesh
    {
        get => _activeMesh;
        set => SetAndRaise(ActiveMeshProperty, ref _activeMesh, value);
    }

    public static readonly DirectProperty<MeshPreviewerControl, string?> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<MeshPreviewerControl, string?>(
            nameof(StatusText), o => o.StatusText, (o, v) => o.StatusText = v);

    private string? _statusText;
    public string? StatusText
    {
        get => _statusText;
        set => SetAndRaise(StatusTextProperty, ref _statusText, value);
    }

    public int WireFrameMode => _wireFrameMode;
    public int ShadeMode => _shadeMode;
    public bool UseAssetNormals => _useAssetNormals;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct GpuVertex
    {
        public Vector3 Position;
        public Vector3 NormalAsset;
        public Vector3 NormalCalc;
        public Vector4 Color;
    }

    private void ActiveMesh_Changed(AvaloniaPropertyChangedEventArgs<MeshObj?> args)
    {
        var mesh = args.NewValue.Value;
        if (mesh == null || mesh.VertexCount == 0)
        {
            _renderData = null;
            _gpuVertices = [];
            _triangleIndices = [];
            _wireframeIndices = [];
            StatusText = null;
            return;
        }

        _orbit = new Vector2(-MathF.PI / 4, -MathF.PI / 6);
        _pan = Vector2.Zero;
        _cameraDistance = 2.5f;
        RecalculateCamera();

        LoadMeshGeometry(mesh);
        Focus();
    }

    private void LoadMeshGeometry(MeshObj mesh)
    {
        _renderData = MeshPreviewBuilder.Build(mesh);
        if (_renderData == null)
        {
            _gpuVertices = [];
            _triangleIndices = [];
            _wireframeIndices = [];
            StatusText = "Mesh has no displayable geometry.";
            return;
        }

        _modelMatrix = _renderData.ModelMatrix;
        _gpuVertices = new GpuVertex[_renderData.VertexCount];
        for (var i = 0; i < _renderData.VertexCount; i++)
        {
            var v = _renderData.Vertices[i];
            _gpuVertices[i] = new GpuVertex
            {
                Position = v.Position,
                NormalAsset = v.Normal,
                NormalCalc = v.CalculatedNormal,
                Color = v.Color
            };
        }

        _triangleIndices = _renderData.TriangleIndices;
        _wireframeIndices = _renderData.WireframeIndices;
        UpdateStatusText();
        _dirtyModel = true;
    }

    private void UpdateStatusText()
    {
        if (_renderData == null)
        {
            StatusText = null;
            return;
        }

        var wf = _wireFrameMode switch
        {
            1 => "Wireframe",
            2 => "Shaded+Wire",
            _ => "Shaded"
        };
        var norm = _useAssetNormals ? "Asset normals" : "Calculated normals";
        StatusText =
            $"Vertices: {_renderData.VertexCount}  Triangles: {_renderData.TriangleCount}  |  {wf}  {norm}\n" +
            "LMB: rotate  RMB: pan  Wheel: zoom  |  W: wire  S: shade  N: normals  R: reset view";
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _leftDown = true;
            var curPos = pt.Position;
            _lastPos = new Vector2((float)curPos.X, (float)curPos.Y);
            Focus();
        }
        else if (pt.Properties.IsRightButtonPressed)
        {
            _rightDown = true;
            var curPos = pt.Position;
            _lastPos = new Vector2((float)curPos.X, (float)curPos.Y);
            Focus();
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
            _leftDown = false;
        if (e.InitialPressMouseButton == MouseButton.Right)
            _rightDown = false;
        if (!_leftDown && !_rightDown)
        {
            _lastPos = new Vector2(-1f, -1f);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_lastPos.X < 0)
            return;

        var curPos = e.GetPosition(this);
        var cur = new Vector2((float)curPos.X, (float)curPos.Y);
        var dx = _lastPos.X - cur.X;
        var dy = cur.Y - _lastPos.Y;

        if (_leftDown)
        {
            _orbit.X += dx * 0.006f;
            _orbit.Y += dy * 0.006f;
            _orbit.Y = MathF.Max(MathF.Min(_orbit.Y, PIH_MINUS_EPSILON), -PIH_MINUS_EPSILON);
            RecalculateCamera();
        }
        else if (_rightDown)
        {
            var panScale = _cameraDistance * 0.002f;
            _pan.X += dx * panScale;
            _pan.Y += dy * panScale;
            RecalculateCamera();
        }

        _lastPos = cur;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _cameraDistance *= 1f - (float)e.Delta.Y / 10f;
        _cameraDistance = MathF.Max(0.05f, MathF.Min(_cameraDistance, 500f));
        RecalculateCamera();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.W:
                    _wireFrameMode = (_wireFrameMode + 1) % 3;
                    break;
                case Key.S:
                    _shadeMode = (_shadeMode + 1) % 2;
                    break;
                case Key.N:
                    _useAssetNormals = !_useAssetNormals;
                    _normalMode = _useAssetNormals ? 0 : 1;
                    if (_activeMesh != null)
                        LoadMeshGeometry(_activeMesh);
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else if (e.Key == Key.R)
        {
            _orbit = new Vector2(-MathF.PI / 4, -MathF.PI / 6);
            _pan = Vector2.Zero;
            _cameraDistance = 2.5f;
            RecalculateCamera();
        }
        else
        {
            handled = false;
        }

        if (handled)
        {
            UpdateStatusText();
            e.Handled = true;
            RequestNextFrameRendering();
        }
    }

    private void RecalculateCamera()
    {
        _cameraPos.X = _cameraDistance * MathF.Cos(_orbit.Y) * MathF.Sin(_orbit.X);
        _cameraPos.Y = _cameraDistance * MathF.Sin(_orbit.Y);
        _cameraPos.Z = _cameraDistance * MathF.Cos(_orbit.Y) * MathF.Cos(_orbit.X);
    }

    public bool HitTest(Point point) => true;

    private void CheckError(int id)
    {
        if (_gl is null || !_loaded)
            return;

        GLEnum err;
        while ((err = _gl.GetError()) != GLEnum.NoError)
            Debug.WriteLine($"OGL Error {err} @ {id}");
    }

    private uint LoadShader(ShaderType shaderType, string content)
    {
        if (_gl is null)
            return uint.MaxValue;

        var shaderHnd = _gl.CreateShader(shaderType);
        _gl.ShaderSource(shaderHnd, content);
        _gl.CompileShader(shaderHnd);
        var infoLog = _gl.GetShaderInfoLog(shaderHnd);
        if (!string.IsNullOrWhiteSpace(infoLog))
            throw new Exception($"Error compiling shader {shaderType}: {infoLog}");

        return shaderHnd;
    }

    protected override unsafe void OnOpenGlInit(GlInterface glInterface)
    {
        if (_loaded)
            return;

        _loaded = true;
        base.OnOpenGlInit(glInterface);

        _gl = GL.GetApi(glInterface.GetProcAddress);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);

        var vs = LoadShader(ShaderType.VertexShader, MeshPreviewerShaders.VERTEX_SOURCE);
        var fs = LoadShader(ShaderType.FragmentShader, MeshPreviewerShaders.FRAGMENT_SOURCE);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vs);
        _gl.AttachShader(_shaderProgram, fs);
        _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.POSITION_LOC, "aPos");
        _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.NORMAL_ASSET_LOC, "aNormalAsset");
        _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.NORMAL_CALC_LOC, "aNormalCalc");
        _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.COLOR_LOC, "aColor");
        _gl.LinkProgram(_shaderProgram);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        CheckError(0);

        _vertexArrayObject = _gl.GenVertexArray();
        _vertexBufferObject = _gl.GenBuffer();
        _indexBufferObject = _gl.GenBuffer();
        _wireBufferObject = _gl.GenBuffer();
    }

    private unsafe void UploadMesh(GL gl)
    {
        if (_gpuVertices.Length == 0)
            return;

        var vertexSize = (uint)Marshal.SizeOf<GpuVertex>();
        gl.BindVertexArray(_vertexArrayObject);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBufferObject);
        fixed (void* pdata = _gpuVertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_gpuVertices.Length * vertexSize), pdata, BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBufferObject);
        fixed (void* pidx = _triangleIndices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_triangleIndices.Length * sizeof(uint)), pidx, BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(GLEnum.ElementArrayBuffer, _wireBufferObject);
        fixed (void* pw = _wireframeIndices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_wireframeIndices.Length * sizeof(uint)), pw, BufferUsageARB.StaticDraw);
        }

        gl.VertexAttribPointer(MeshPreviewerShaders.POSITION_LOC, 3, GLEnum.Float, false, vertexSize, (void*)0);
        gl.VertexAttribPointer(MeshPreviewerShaders.NORMAL_ASSET_LOC, 3, GLEnum.Float, false, vertexSize, (void*)12);
        gl.VertexAttribPointer(MeshPreviewerShaders.NORMAL_CALC_LOC, 3, GLEnum.Float, false, vertexSize, (void*)24);
        gl.VertexAttribPointer(MeshPreviewerShaders.COLOR_LOC, 4, GLEnum.Float, false, vertexSize, (void*)36);

        gl.EnableVertexAttribArray(MeshPreviewerShaders.POSITION_LOC);
        gl.EnableVertexAttribArray(MeshPreviewerShaders.NORMAL_ASSET_LOC);
        gl.EnableVertexAttribArray(MeshPreviewerShaders.NORMAL_CALC_LOC);
        gl.EnableVertexAttribArray(MeshPreviewerShaders.COLOR_LOC);
        CheckError(2);
    }

    protected override unsafe void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        var gl = GL.GetApi(glInterface.GetProcAddress);

        if (_dirtyModel)
        {
            _dirtyModel = false;
            UploadMesh(gl);
        }

        gl.ClearColor(0.08f, 0.09f, 0.11f, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        var w = Math.Max(1, (int)Bounds.Width);
        var h = Math.Max(1, (int)Bounds.Height);
        gl.Viewport(0, 0, (uint)w, (uint)h);

        if (_gpuVertices.Length == 0 || _triangleIndices.Length == 0)
        {
            RequestNextFrameRendering();
            return;
        }

        gl.BindVertexArray(_vertexArrayObject);
        gl.UseProgram(_shaderProgram);

        var aspect = (float)w / h;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.01f, 1000f);
        var target = new Vector3(_pan.X, _pan.Y, 0f);
        var viewMatrix = Matrix4x4.CreateLookAt(_cameraPos + target, target, Vector3.UnitY);
        var modelMatrix = _modelMatrix;
        var projMatrix = projection;

        var modelLoc = gl.GetUniformLocation(_shaderProgram, "uModel");
        var viewLoc = gl.GetUniformLocation(_shaderProgram, "uView");
        var projLoc = gl.GetUniformLocation(_shaderProgram, "uProjection");
        var lightDirLoc = gl.GetUniformLocation(_shaderProgram, "uDirectionalLightDir");
        var lightColorLoc = gl.GetUniformLocation(_shaderProgram, "uDirectionalLightColor");
        var normalSrcLoc = gl.GetUniformLocation(_shaderProgram, "uNormalSource");
        var shadeLoc = gl.GetUniformLocation(_shaderProgram, "uShadeMode");
        var passLoc = gl.GetUniformLocation(_shaderProgram, "uPassMode");

        gl.UniformMatrix4(modelLoc, 1, false, &modelMatrix.M11);
        gl.UniformMatrix4(viewLoc, 1, false, &viewMatrix.M11);
        gl.UniformMatrix4(projLoc, 1, false, &projMatrix.M11);
        gl.Uniform3(lightDirLoc, -0.6f, -0.8f, -0.5f);
        gl.Uniform3(lightColorLoc, 1f, 1f, 1f);
        gl.Uniform1(normalSrcLoc, _useAssetNormals ? 0 : 1);
        gl.Uniform1(shadeLoc, _shadeMode);

        if (_wireFrameMode != 1)
        {
            gl.Uniform1(passLoc, 0);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBufferObject);
            gl.DrawElements(PrimitiveType.Triangles, (uint)_triangleIndices.Length, DrawElementsType.UnsignedInt, (void*)0);
        }

        if (_wireFrameMode != 0)
        {
            gl.Uniform1(passLoc, 1);
            gl.LineWidth(1f);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _wireBufferObject);
            gl.DrawElements(PrimitiveType.Lines, (uint)_wireframeIndices.Length, DrawElementsType.UnsignedInt, (void*)0);
        }

        CheckError(10);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface glInterface)
    {
        if (_gl is null)
            return;

        var gl = _gl;
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
        gl.BindVertexArray(0);
        gl.UseProgram(0);

        if (_vertexBufferObject != 0) gl.DeleteBuffer(_vertexBufferObject);
        if (_indexBufferObject != 0) gl.DeleteBuffer(_indexBufferObject);
        if (_wireBufferObject != 0) gl.DeleteBuffer(_wireBufferObject);
        if (_vertexArrayObject != 0) gl.DeleteVertexArray(_vertexArrayObject);
        if (_shaderProgram != 0) gl.DeleteProgram(_shaderProgram);
    }
}