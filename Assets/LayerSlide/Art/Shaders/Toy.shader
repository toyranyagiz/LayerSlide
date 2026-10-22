// Molded-plastic toy look for blocks, tiles, walls and the tray (URP, forward).
// Top faces use _BaseColor, vertical faces _SideColor; the bevel blends between them from the normal.
// UV1.y marks the raised inner panel on top caps (1 inside, 0 at the rim). Soft wrapped diffuse, a tinted
// shadow side, a small plastic highlight, a gentle rim and an optional contact darkening near the board.
Shader "LayerSlide/Toy"
{
    Properties
    {
        _BaseColor ("Top Color", Color) = (1, 1, 1, 1)
        _SideColor ("Side Color", Color) = (0.75, 0.75, 0.75, 1)
        _ShadowTint ("Shadow Tint", Color) = (0.62, 0.56, 0.90, 1)
        _PanelLight ("Panel Lift", Range(0, 0.4)) = 0.08
        _SpecStrength ("Highlight Strength", Range(0, 1)) = 0.35
        _SpecSize ("Highlight Tightness", Range(1, 9)) = 6
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
        _ContactAO ("Contact Darkening", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _SideColor;
            half4 _ShadowTint;
            half _PanelLight;
            half _SpecStrength;
            half _SpecSize;
            half _RimStrength;
            half _ContactAO;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 shade : TEXCOORD2; // x: side amount, y: panel, z: object-space normal.y
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                float up = saturate(input.normalOS.y);
                output.shade = float3(1.0 - up, input.uv1.y, up);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // Albedo: the top color holds across the flat cap, the bevel eases into the side color.
                half side = smoothstep(0.08, 0.85, input.shade.x);
                half3 albedo = lerp(_BaseColor.rgb, _SideColor.rgb, side);
                albedo += _PanelLight * input.shade.y * (1.0 - side);

                // Contact darkening on vertical faces close to the board (world Y ~ 0).
                half contact = saturate(input.positionWS.y / 0.14);
                albedo *= lerp(1.0, lerp(1.0 - _ContactAO, 1.0, contact), side);

                // Soft wrapped light with a tinted (not grey) shadow side; cast shadows use the same tint.
                half ndl = dot(n, light.direction);
                half wrap = smoothstep(0.0, 1.0, saturate(ndl * 0.6 + 0.4));
                half shadow = lerp(0.35, 1.0, light.shadowAttenuation);
                half lit = wrap * shadow;
                half3 color = albedo * lerp(_ShadowTint.rgb, 1.0, lit) * lerp(0.88, 1.0, lit);

                // Plastic highlight and rim.
                half3 h = SafeNormalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), exp2(_SpecSize)) * _SpecStrength * light.shadowAttenuation;
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _RimStrength * (0.4 + 0.6 * input.shade.z);
                color += spec + rim;
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
