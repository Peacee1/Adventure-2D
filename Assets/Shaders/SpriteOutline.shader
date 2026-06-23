Shader "Custom/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,0,0)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0

        // Outline properties
        [MaterialToggle] _Outline ("Outline Enabled", Float) = 1
        _OutlineColor ("Outline Color", Color) = (1,1,1,0.5) // Semi-transparent by default for a "light" outline
        _OutlineSize ("Outline Size", Range(0, 5)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineSize;
            float _Outline;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap (OUT.vertex);
                #endif

                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;

                if (_Outline > 0.0)
                {
                    // Check alpha in adjacent pixels
                    // Up, down, left, right, and diagonals for a smoother outline
                    float3 offset = float3(_MainTex_TexelSize.xy, 0.0) * _OutlineSize;
                    
                    float alphaUp = tex2D(_MainTex, IN.texcoord + offset.zy).a;
                    float alphaDown = tex2D(_MainTex, IN.texcoord - offset.zy).a;
                    float alphaRight = tex2D(_MainTex, IN.texcoord + offset.xz).a;
                    float alphaLeft = tex2D(_MainTex, IN.texcoord - offset.xz).a;

                    float alphaUpRight = tex2D(_MainTex, IN.texcoord + offset.xy).a;
                    float alphaUpLeft = tex2D(_MainTex, IN.texcoord + float2(-offset.x, offset.y)).a;
                    float alphaDownRight = tex2D(_MainTex, IN.texcoord + float2(offset.x, -offset.y)).a;
                    float alphaDownLeft = tex2D(_MainTex, IN.texcoord - offset.xy).a;

                    float maxAlpha = max(max(max(alphaUp, alphaDown), max(alphaRight, alphaLeft)), 
                                         max(max(alphaUpRight, alphaUpLeft), max(alphaDownRight, alphaDownLeft)));

                    // If this pixel is transparent or semi-transparent, but adjacent ones are not, draw outline
                    if (c.a < 0.1 && maxAlpha > 0.1)
                    {
                        c = _OutlineColor;
                        c.rgb *= c.a; // Premultiplied alpha
                    }
                }

                return c;
            }
        ENDCG
        }
    }
}
