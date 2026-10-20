// The Earth, as a surface rather than a lit photograph: day/night, city lights, and an atmosphere
// that is a property of the sphere instead of a ring pasted around it.
//
// The positions arrive in CLIP SPACE. The C# side transforms the unit sphere by the body's
// orientation and the view-projection before it fills the vertex buffer. This is deliberate: the
// effect compiler in this project exposes a `float4x4` uniform as a scalar (that is why the Sun's
// corona never drew until its quad was projected on the CPU), so a body shader that needs a matrix
// gets the same treatment. The sphere is 2,145 vertices; transforming it once per frame costs
// nothing next to the pixel work.
//
// The world position is kept because the atmosphere is a function of the angle between the surface
// normal and the direction to the camera. The camera is at the origin of render space, so the view
// direction is just the normalised negative world position — no camera uniform, no matrix.

Texture2D Albedo;
SamplerState AlbedoSampler;
Texture2D Night;
SamplerState NightSampler;
Texture2D Clouds;
SamplerState CloudSampler;

// Direction from the body towards the Sun, unit length; the w is unused.
float4 SunDirection;

// RGB of the scattering; the w is unused.
float4 AtmosphereColor;

// x = city-light brightness, y = cloud coverage, z = atmosphere strength. Packed into one float4
// because the compiler's constant-buffer layout needs the vectors on sixteen-byte boundaries; a
// bare float between two float3s is not expressible as std140.
float4 Tuning;

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float3 World    : TEXCOORD0;
    float2 UV       : TEXCOORD1;
};

struct VertexOutput
{
    float4 Position : POSITION0;
    float3 Normal   : TEXCOORD0;
    float3 World    : TEXCOORD1;
    float2 UV       : TEXCOORD2;
};

VertexOutput VertexMain(VertexInput input)
{
    VertexOutput output;
    output.Position = input.Position;
    output.Normal = input.Normal;
    output.World = input.World;
    output.UV = input.UV;
    return output;
}

// The sphere's UVs run 0.5..1.5 so that longitude zero lands on the map's prime meridian. Wrapping
// here keeps the texture coordinates inside [0,1] whatever sampler state the effect ends up with;
// the seam is at the back of the sphere and the two edge vertices carry the same u.
float2 MapUV(float2 uv)
{
    return float2(frac(uv.x), uv.y);
}

float4 SurfaceMain(VertexOutput input) : COLOR0
{
    float3 n = normalize(input.Normal);
    float3 toSun = normalize(SunDirection.xyz);
    float2 uv = MapUV(input.UV);

    float ndl = dot(n, toSun);

    // A soft terminator: the day side's last few degrees are twilight, not a hard cut. The city
    // lights fade in over the same band, so the two never both claim the same pixel at full strength.
    float day = smoothstep(-0.12, 0.18, ndl);

    float3 albedo = Albedo.Sample(AlbedoSampler, uv).rgb;
    float3 lights = Night.Sample(NightSampler, uv).rgb * Tuning.x * (1.0 - day);

    // One sun and a trace of ambient, the same rule the hulls follow. The night side is dark; the
    // city lights are the only thing that emits on it.
    float3 lit = albedo * (0.02 + saturate(ndl));

    // A weak ocean glint. Most of the day map's blue is water, and a sun-facing sea is the one place
    // a planet reads as a sphere before the terminator does.
    float3 viewDir = normalize(-input.World);
    float3 halfDir = normalize(toSun + viewDir);
    float specular = pow(saturate(dot(n, halfDir)), 48.0) * 0.14 * saturate(ndl);

    // The cloud deck is sampled here rather than drawn as a second sphere. A separate shell has to
    // be lit by the fixed-function sun, and on the night side it stayed white because that light
    // path is a different shader; in one shader the clouds share the terminator with the ground.
    float4 cloudSample = Clouds.Sample(CloudSampler, uv);
    float cloud = saturate(cloudSample.a * Tuning.y);
    float3 cloudColour = cloudSample.rgb * (0.02 + saturate(ndl)) * 1.10;

    // The city lights sit under the cloud deck: a thick cloud hides most of a city.
    float3 surface = lit + specular + lights * (1.0 - cloud * 0.85);

    // The inner half of the atmosphere: the light that scatters through the limb before it leaves.
    // The shell below draws the outer half. This term is deliberately weak; it should tint the edge
    // of the disc, not paint a blue ring on the ground.
    float rim = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
    float sunward = saturate(ndl + 0.15);
    float3 atmosphere = AtmosphereColor.rgb * rim * sunward * Tuning.z * 0.18;

    return float4(lerp(surface, cloudColour, cloud) + atmosphere, 1.0);
}

float4 AtmosphereMain(VertexOutput input) : COLOR0
{
    float3 n = normalize(input.Normal);
    float3 viewDir = normalize(-input.World);

    // The shell is 2% larger than the planet, so its own limb is where the haze is longest. The
    // Fresnel term is what makes the edge read as a limb rather than a decal.
    float rim = pow(1.0 - saturate(dot(n, viewDir)), 5.0);

    // The atmosphere is lit by the Sun: a night-side limb is not a blue ring, it is black.
    float sunward = smoothstep(-0.25, 0.25, dot(n, normalize(SunDirection.xyz)));
    float alpha = rim * sunward * Tuning.z;

    // Additive blending multiplies by this alpha, so the colour is not premultiplied here.
    return float4(AtmosphereColor.rgb, alpha);
}

technique Surface
{
    pass
    {
        VertexShader = compile vs_3_0 VertexMain();
        PixelShader  = compile ps_3_0 SurfaceMain();
    }
};

technique Atmosphere
{
    pass
    {
        VertexShader = compile vs_3_0 VertexMain();
        PixelShader  = compile ps_3_0 AtmosphereMain();
    }
};
