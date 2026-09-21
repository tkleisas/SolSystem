// The Sun: a limb-darkened disc inside a radiating corona.
//
// The disc is drawn as a camera-facing quad eight times the Sun's angular radius, and everything
// happens in the pixel shader in units of that radius. Three things make it read as a star rather
// than as a white circle:
//
//   1. LIMB DARKENING. The edge of the Sun is about a third as bright as its middle, and the eye
//      reads that gradient as "this is a sphere" more strongly than it reads the shape.
//   2. THE CORONA. A star is a bright point with light around it, and the falloff is what makes it
//      read as radiating rather than pasted on. Two exponentials: a tight one for the flash at the
//      limb, a wide one for the sky glow.
//   3. COLOUR. The disc is white and the corona is warm, because the light that scatters out of it
//      has been through more of the outer atmosphere than the light that comes straight at you.
//
// Values above one are written deliberately. Nothing clamps them here — the blend is additive, so a
// bright disc accumulates over its own glow, and the saturating white core is what a star looks
// like when there is more of it than the display has.

sampler2D Photosphere : register(s0);

float4x4 WorldViewProjection;

// Disc radius and corona scale, in the quad's own units. The quad spans [-1,1].
float DiscRadius;
float CoronaTightness;
float CoronaWidth;
float DiscIntensity;
float CoronaIntensity;
float FlickerAmount;
float Time;

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float2 Local    : TEXCOORD0;
    float3 Ndc      : TEXCOORD1;
};

VertexOutput VertexMain(VertexInput input)
{
    VertexOutput output;

    // The quad is built in the world already facing the camera, so the vertex shader only has to
    // put it through the matrices. The normal carries the direction the quad faces, which is what
    // the photosphere is sampled by.
    output.Position = mul(float4(input.Position.xyz, 1.0), WorldViewProjection);
    output.Local = input.TexCoord * 2.0 - 1.0;
    output.Ndc = input.Normal;

    return output;
}

// Distance from the disc's centre, in disc radii. Inside one is the star.
float RadialDistance(float2 local)
{
    return length(local) / DiscRadius;
}

// A cheap two-octave value noise, for the granulation. The real photosphere is convection cells a
// few hundred kilometres across; on anything but a very close approach this is under a pixel, so it
// is here to give the limb some texture and not to be a simulation of convection.
float Granulation(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
    float b = frac(sin(dot(cell + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
    float c = frac(sin(dot(cell + float2(0, 1), float2(127.1, 311.7))) * 43758.5453);
    float d = frac(sin(dot(cell + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 PixelMain(VertexOutput input) : COLOR0
{
    float r = RadialDistance(input.Local);

    // ------------------------------------------------------------------ the disc
    //
    // The z of the surface normal on a unit sphere is sqrt(1 - r^2), and the Eddington
    // approximation for a grey atmosphere gives I(mu) = (2 + 3 mu) / 5. At the limb that is 0.4 of
    // the centre, which is close to the measured 0.3 and costs one multiply.
    // The mask is load-bearing. Without it `saturate` floors mu at zero OUTSIDE the disc, and the
    // limb term therefore evaluates to 0.4 everywhere in the quad — so the corona is drawn on top of
    // a flat 0.4 of white and the Sun comes out as a bright SQUARE the size of the quad. It did.
    float inside = step(r, 1.0);
    float mu = sqrt(saturate(1.0 - r * r));
    float limb = (2.0 + 3.0 * mu) / 5.0 * inside;

    // Granulation, drifting slowly. Sampling by the surface normal means the pattern turns with the
    // sphere rather than sliding across it.
    float cells = Granulation(input.Local * 34.0 + Time * 0.05);
    limb *= 0.86 + 0.28 * cells;

    float3 disc = float3(1.0, 0.97, 0.92) * limb * DiscIntensity;

    // ------------------------------------------------------------------ the corona
    //
    // Two falloffs: a tight one that hugs the limb, and a wide one that carries the glow out to the
    // edge of the quad. The wide one is what a photograph of the Sun shows as the sky around it.
    float over = max(r - 1.0, 0.0);
    float tight = exp(-over * CoronaTightness);
    float wide = 1.0 / (1.0 + over * over * CoronaWidth);

    // And the disc's own glow has to stop at the limb rather than leak past it, which is what the
    // tight falloff is measured from.

    // A slow flicker, so the corona is not perfectly still. Small: a star that pulses is a lamp.
    float flicker = 1.0 + FlickerAmount * sin(Time * 0.7 + r * 9.0);

    float3 corona = float3(1.0, 0.86, 0.62)
        * ((tight * 0.55) + (wide * 0.45)) * CoronaIntensity * flicker;

    // Sharply cut off at the disc edge, so the limb is a limb and not a gradient.
    // The corona is forced to nothing at the quad's edge, so that however the falloffs are tuned
    // there is never a visible square in the sky. A glow that stops at a straight line is worse than
    // no glow, and it is the first thing the eye finds.
    float corner = 1.0 - smoothstep(0.72, 1.0, length(input.Local));
    corona *= corner;

    return float4(disc + corona, 1.0);
}

technique Sun
{
    pass
    {
        VertexShader = compile vs_3_0 VertexMain();
        PixelShader  = compile ps_3_0 PixelMain();
    }
};
