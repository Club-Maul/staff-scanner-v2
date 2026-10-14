Shader "Unlit/Scanner"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _NormalOpacity ("Normal Opacity", Range(0, 1)) = 0.5
        _OccludedOpacity ("Occluded Opacity", Range(0, 1)) = 0.3
        _NormalRange ("Normal Range", Float) = 30
        _OccludedRange ("Occluded Range", Float) = 10
        _OcclusionDepthStart ("Occlusion Depth Start", Float) = 1
        _OcclusionDepthEnd ("Occlusion Depth End", Float) = 3
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Overlay+500"
        }

        ZWrite Off
        ZTest Off
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 clip_pos: TEXCOORD0;
                float3 world_pos : TEXCOORD1;
            };

            float4 _Color;
            float _NormalOpacity;
            float _OccludedOpacity;
            float _NormalRange;
            float _OccludedRange;
            float _OcclusionDepthStart;
            float _OcclusionDepthEnd;

            Texture2D _CameraDepthTexture;
            SamplerState sampler_linear_repeat;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.world_pos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.clip_pos = o.vertex;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 screen_pos = ComputeScreenPos(i.clip_pos / i.clip_pos.w);
                float raw_depth = _CameraDepthTexture.Sample(sampler_linear_repeat, screen_pos.xy);

                float depth = LinearEyeDepth(raw_depth);
                float range = distance(i.world_pos, _WorldSpaceCameraPos);

                float delta = range - depth;
                float occlusion = smoothstep(_OcclusionDepthStart, _OcclusionDepthEnd, delta);
                
                float normal_alpha = smoothstep(_NormalRange, _NormalRange * 0.9, range);
                float occluded_alpha = smoothstep(_OccludedRange, _OccludedRange * 0.9, range);
                
                float alpha = lerp(normal_alpha, occluded_alpha, occlusion);
                
                float4 result = _Color;
                
                result.a = alpha * lerp(_NormalOpacity, _OccludedOpacity, occlusion);
                
                return result;
            }
            ENDCG
        }
    }
}