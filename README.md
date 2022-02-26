# NanoSurf

A small surface shader implementation for URP.

## Usage

```cs
SHADER(Shader Name)
TYPE({Lit, Unlit, Decal})

PROPERTY(float3 _Color)
PROPERTY(float _Property)
PROPERTY(sampler2d _Texture)

PASS(ALL)
    #include "Something.cginc"
    float do_something() { return 0; }
ENDPASS()

PASS()
    void ns_vert(NS_VERT_PARAMS) {
        // Your vertex shader code goes here
    }

    void ns_frag(NS_FRAG_PARAMS) {
        // Your fragment shader code goes here
        color = float4(_Color, do_something());
    }
ENDPASS()

PASS(Special Pass)
    
ENDPASS()
```

Note:
- Special pass names include:
    - Blank: *Surface* Pass, basically a custom shader graph function
    - ALL: Included in all passes
- All other passes are given a tag and lightmode with their name
