using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// Draws a hull: its parts, their materials, and the light falling on them.
/// </summary>
/// <remarks>
/// <para>
/// A hull is lit by one thing, which is the Sun. There is no fill light and no ambient to speak of,
/// because there is no sky to bounce anything — and the consequence is worth stating plainly, since
/// it is the look of the whole game: <b>a ship in the outer system is nearly black</b>, lit on one
/// side by a Sun thirty times dimmer than the Earth's, and the only things on it that are not are
/// the radiators, which are glowing because they have to be. That is not a rendering limitation, it
/// is what the setting is.
/// </para>
/// <para>
/// The material model is the fixed-function one: a diffuse colour, plus a specular highlight whose
/// tightness comes from the material's roughness. It is not a microfacet BRDF and does not pretend
/// to be — but the Illuminus finish is polished and the Workers finish is not, and the difference
/// between a hull that glints and a hull that does not is most of the visual identity the two
/// factions have.
/// </para>
/// </remarks>
internal sealed class HullRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;

    internal HullRenderer(GraphicsDevice device)
    {
        _device = device;

        _effect = new BasicEffect(device)
        {
            TextureEnabled = false,
            VertexColorEnabled = false,
            LightingEnabled = true,
            World = Matrix.Identity,
            AmbientLightColor = new Vector3(0.03f),
            SpecularColor = Vector3.Zero,
        };

        _effect.DirectionalLight0.Enabled = true;

        // THE SECOND LIGHT IS EARTHSHINE, and it is not a fill light wearing a costume.
        //
        // A hull four hundred metres above the Earth is lit by two things: the Sun, and the
        // sixty-nine per cent of the sunlight the planet bounces back. Earth's Bond albedo is 0.306,
        // the planet fills half the sky from low orbit, and the result is that the night side of a
        // spacecraft in LEO is very obviously not black — it is a deep blue-grey, and every photograph
        // taken from the Shuttle or the station shows it.
        //
        // Without it, half of every hull in this game renders as a silhouette, which is correct for a
        // ship in deep space and wrong for one at a planet. The first version of this had one light
        // and the player's ship was a black shape against a black sky.
        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight2.Enabled = false;
        _effect.PreferPerPixelLighting = true;
    }

    /// <summary>
    /// How many parts were drawn this frame, for the heads-up display.
    /// </summary>
    /// <remarks>
    /// ACCUMULATED ACROSS DRAW CALLS, and reset once a frame. It was per-call, and the station —
    /// which has no navigation lights — was drawn second and overwrote the count with zero. The
    /// display said "0 lit" for a ship whose lights were on, which is worse than not having the
    /// readout: it is a measurement that lies.
    /// </remarks>
    internal int PartsDrawn { get; private set; }

    /// <summary>How many navigation lights were lit this frame, for the heads-up display.</summary>
    internal int LightsLit { get; private set; }

    /// <summary>Clears the per-frame counters. Called once, before anything is drawn.</summary>
    internal void BeginFrame()
    {
        PartsDrawn = 0;
        LightsLit = 0;
    }

    /// <summary>
    /// The clock the navigation lights flash on.
    /// </summary>
    /// <remarks>
    /// Set once per frame by the caller, because a renderer that reads the wall clock cannot be
    /// rendered twice at the same instant and produce the same frame — and this project checks its
    /// frames.
    /// </remarks>
    internal double Seconds { get; set; }

    /// <summary>
    /// Draws a hull.
    /// </summary>
    /// <param name="hull">Which hull.</param>
    /// <param name="world">Where it is in render space, and which way it is pointing.</param>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="sunDirection">Unit vector from the hull towards the Sun, in render space.</param>
    /// <param name="earthDirection">
    /// Unit vector from the hull towards the planet it is orbiting, or zero in deep space.
    /// </param>
    /// <param name="earthshine">
    /// How much light the planet throws back: its Bond albedo times how much sky it fills.
    /// </param>
    internal void Draw(Hull hull, Matrix world, Matrix view, Matrix projection,
        Vector3 sunDirection, Vector3 earthDirection, float earthshine)
    {
        _effect.View = view;
        _effect.Projection = projection;
        _effect.DirectionalLight0.Direction = Vector3.Normalize(sunDirection);
        _effect.DirectionalLight0.DiffuseColor = Vector3.One;

        // Blue, because what the Earth reflects is a blue planet's light, and scaled by how big it
        // is in the sky — a hull in low orbit gets the full figure and one at the Moon gets none.
        if (earthshine > 0.001f && earthDirection.LengthSquared() > 1e-6f)
        {
            _effect.DirectionalLight1.Enabled = true;
            _effect.DirectionalLight1.Direction = Vector3.Normalize(earthDirection);
            _effect.DirectionalLight1.DiffuseColor = new Vector3(0.42f, 0.52f, 0.72f) * earthshine;
        }
        else
        {
            _effect.DirectionalLight1.Enabled = false;
        }

        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;

        // Back faces are culled, which is what a closed hull wants. A part whose material says
        // doubleSided turns it off for that part — a radiator panel is a plane and has no inside.
        _device.RasterizerState = RasterizerState.CullCounterClockwise;

        int drawn = 0;

        foreach (Hull.Part part in hull.Parts)
        {
            Hull.Material material = part.Material;
            Matrix transform = part.WorldTransform * world;

            _effect.World = transform;
            _device.RasterizerState = material.DoubleSided
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;

            Texture2D? texture = material.BaseColorTexture;
            _effect.Texture = texture;
            _effect.TextureEnabled = texture is not null;

            Vector4 baseColour = material.BaseColorFactor;

            // NAVIGATION LIGHTS ARE CHECKED FIRST, and the order is the whole fix.
            //
            // They are emissive, so `material.Emits` catches them — and the branch below then draws
            // them steadily, which is not the convention, and never reaches the flash schedule. The
            // display read "0 lit" while the lights were in fact on, permanently, which is the most
            // confusing possible combination: a readout that is wrong about a thing that is also
            // wrong.
            bool isLight = NavigationLight.IsLight(material.Name);

            if (isLight)
            {
                float flash = NavigationLight.Brightness(material.Name, Seconds);

                _effect.AmbientLightColor = new Vector3(0.03f);
                _effect.DiffuseColor = new Vector3(baseColour.X, baseColour.Y, baseColour.Z);
                _effect.EmissiveColor = material.EmissiveFactor * flash;

                if (flash > 0.5f)
                {
                    LightsLit++;
                }
            }
            else if (material.Emits)
            {
                // A radiator or a nozzle: drawn as its own light rather than as a surface something
                // is shining on. The emissive term is the whole of it, and it is what makes a hull
                // read as *running* rather than as a model of a hull.
                _effect.AmbientLightColor = Vector3.Zero;
                _effect.DiffuseColor = Vector3.Zero;
                _effect.EmissiveColor = material.EmissiveFactor;
            }
            else
            {
                _effect.AmbientLightColor = new Vector3(0.03f);
                _effect.DiffuseColor = new Vector3(baseColour.X, baseColour.Y, baseColour.Z);

                // Roughness to a specular exponent: a smooth surface has a small tight highlight and
                // a rough one has none. The mapping is the usual one and it is not physical — it is
                // a fixed-function approximation of a microfacet lobe, and it is here because
                // polished steel and painted plate have to look different.
                float roughness = Math.Clamp(material.Roughness, 0.04f, 1.0f);
                float exponent = (2.0f / (roughness * roughness * roughness * roughness)) - 2.0f;

                _effect.SpecularPower = Math.Clamp(exponent, 1.0f, 200.0f);

                float gloss = (1.0f - roughness) * (1.0f - roughness);
                _effect.SpecularColor = new Vector3(gloss * 0.9f);

                _effect.EmissiveColor = Vector3.Zero;
            }

            _device.Indices = part.Indices;
            _device.SetVertexBuffer(part.Vertices);

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, part.Triangles);
            }

            drawn++;
        }

        PartsDrawn += drawn;
    }

    public void Dispose() => _effect.Dispose();
}
