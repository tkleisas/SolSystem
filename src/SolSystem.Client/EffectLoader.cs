using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// Loads a compiled effect from a file.
/// </summary>
/// <remarks>
/// <para>
/// The effect compiles ahead of the content build with ShadowDuskCLI — HLSL through DXC and
/// SPIRV-Cross straight to GLSL, natives shipped for every desktop platform — which is what
/// lets the whole build run with no Wine and no Windows SDK. A compiled <c>.mgfx</c> is a
/// container the <see cref="Effect"/> constructor parses directly, so the content pipeline's
/// XNB wrapper is not part of the read; the pipeline only copies the file where the XNB used
/// to be (see <c>Content/Content.mgcb</c>).
/// </para>
/// <para>
/// A missing effect gets a sentence rather than a header dump. The usual cause is a build that
/// skipped the CompileEffects target, and saying so is more use than the framework's message.
/// </para>
/// </remarks>
internal static class EffectLoader
{
    internal static Effect Load(GraphicsDevice device, string name)
    {
        string path = $"Content/{name}.mgfx";

        try
        {
            using Stream stream = TitleContainer.OpenStream(path);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return new Effect(device, memory.ToArray());
        }
        catch (FileNotFoundException exception)
        {
            throw new FileNotFoundException(
                $"the compiled shader '{path}' is not in the content tree. The build compiles it "
                + "with ShadowDuskCLI in the CompileEffects target before the content build; if "
                + "that was skipped, force a rebuild.", exception);
        }
    }
}
