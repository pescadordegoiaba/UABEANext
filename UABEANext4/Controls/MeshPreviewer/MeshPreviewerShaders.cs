namespace UABEANext4.Controls.MeshPreviewer;

public static class MeshPreviewerShaders
{
    public const string VERTEX_SOURCE = @"#version 300 es
precision mediump float;
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormalAsset;
layout (location = 2) in vec3 aNormalCalc;
layout (location = 3) in vec4 aColor;
uniform mat4 uModel;
uniform mat4 uProjection;
uniform mat4 uView;
uniform int uNormalSource;
out vec3 FragNormal;
out vec4 FragColor;
void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
    FragNormal = uNormalSource == 0 ? aNormalAsset : aNormalCalc;
    FragColor = aColor;
}";

    public const string FRAGMENT_SOURCE = @"#version 300 es
precision mediump float;
in vec3 FragNormal;
in vec4 FragColor;
uniform vec3 uDirectionalLightDir;
uniform vec3 uDirectionalLightColor;
uniform int uShadeMode;
uniform int uPassMode;
out vec4 outColor;
void main()
{
    if (uPassMode == 1) {
        outColor = vec4(0.95, 0.85, 0.2, 1.0);
        return;
    }
    if (uShadeMode == 1) {
        outColor = vec4(FragColor.rgb, 1.0);
        return;
    }
    vec3 normal = normalize(FragNormal);
    vec3 lightDirection = normalize(uDirectionalLightDir);
    float diff = max(dot(normal, lightDirection), 0.0);
    vec3 diffuse = diff * uDirectionalLightColor * FragColor.rgb + 0.25 * FragColor.rgb;
    outColor = vec4(diffuse, 1.0);
}";

    public const int POSITION_LOC = 0;
    public const int NORMAL_ASSET_LOC = 1;
    public const int NORMAL_CALC_LOC = 2;
    public const int COLOR_LOC = 3;
}