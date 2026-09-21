using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// Loads a compiled effect through the content manager.
/// </summary>
/// <remarks>
/// <para>
/// The build runs the MonoGame content pipeline over <c>Content/Content.mgcb</c>, which turns
/// <c>Sun.fx</c> into <c>Sun.xnb</c> beside the executable. An <c>.xnb</c> is not a shader — it is the
/// pipeline's wrapper around one, with a header and a type reader — so handing its bytes to
/// <c>new Effect(...)</c> fails with "this does not appear to be a MonoGame MGFX file", which is
/// exactly what it said the first time. Going through the content manager is what unwraps it.
/// </para>
/// <para>
/// A missing effect gets a sentence rather than a header dump. The usual cause is a build that
/// skipped the pipeline, and saying so is more use than the framework's message.
/// </para>
/// </remarks>
internal static class EffectLoader
{
    internal static Effect Load(ContentManager content, string name)
    {
        try
        {
            return content.Load<Effect>(name);
        }
        catch (ContentLoadException exception)
        {
            throw new FileNotFoundException(
                $"the compiled shader '{name}' is not in the content directory. The build runs the "
                + "MonoGame content pipeline over Content/Content.mgcb; if that was skipped, force a "
                + "rebuild.", exception);
        }
    }
}
