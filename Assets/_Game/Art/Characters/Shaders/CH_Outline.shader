// Thin inverted-hull outline, used as a second material on a renderer (the mesh is drawn once more with it).
// The line colour is a darkened copy of the surface colour, so edges read as shading rather than black ink.
// Width is in metres up to 4 m from the camera and grows to 3x by 12 m, so the silhouette survives at range.
// Set _UseSmoothedNormals for rigid hard-surface meshes whose UV3 holds averaged normals; skinned meshes use NORMAL.
Shader "CoopDeck/Character/Outline"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (line colour source)", 2D) = "white" {}
        _OutlineColor ("Outline Tint", Color) = (0.32, 0.2, 0.18, 1)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.01)) = 0.0018
        _AlbedoInfluence ("Take Colour From Surface", Range(0, 1)) = 0.75
        [Toggle(_OUTLINE_UV3_NORMALS)] _UseSmoothedNormals ("Smoothed Normals In UV3", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _OUTLINE_UV3_NORMALS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _OutlineWidth;
                half _AlbedoInfluence;
                half _UseSmoothedNormals;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS     : POSITION;
                float3 normalOS       : NORMAL;
                float2 uv             : TEXCOORD0;
                float3 smoothNormalOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half fogFactor    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            #if defined(_OUTLINE_UV3_NORMALS)
                float3 normalOS = input.smoothNormalOS;
            #else
                float3 normalOS = input.normalOS;
            #endif
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float distanceScale = clamp(distance(positionWS, GetCameraPositionWS()) / 4.0, 1.0, 3.0);
                positionWS += normalWS * (_OutlineWidth * distanceScale);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 lineColor = lerp(_OutlineColor.rgb, surface * _OutlineColor.rgb * 1.6, _AlbedoInfluence);
                return half4(MixFog(lineColor, input.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
