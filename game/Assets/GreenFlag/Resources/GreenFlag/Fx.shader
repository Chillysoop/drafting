// Step 30: sparks, smoke and skid marks. Unlit, vertex colour times _Tint, blend mode set per material (alpha for smoke and
// rubber, additive for sparks). Lives under Resources so it is always in the player build; FxView loads it by name.
Shader "GreenFlag/Fx"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Tint;
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
    Fallback Off
}
