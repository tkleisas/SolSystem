using Microsoft.Xna.Framework;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// Where the camera is and which way it is looking.
/// </summary>
/// <remarks>
/// <para>
/// A camera in a space game is not a decoration on the simulation — it is the only instrument the
/// player has. Everything else on the screen is a number, and the numbers are meaningless until you
/// can turn round and see what they refer to. The first version of this client had one fixed chase
/// view, which meant a player could accelerate, watch the range change, and have no way to look at
/// the thing they were accelerating towards.
/// </para>
/// <para>
/// So: three modes, a mouse that orbits, and a wheel that zooms. The modes are genuinely different
/// views rather than a nicety — a chase camera shows you the ship, a cockpit shows you where you are
/// going, and an outside view of the target shows you what you are aiming at, and a pilot needs all
/// three at different moments.
/// </para>
/// </remarks>
internal sealed class Camera
{
    /// <summary>
    /// What the camera is attached to.
    /// </summary>
    internal enum Mode
    {
        /// <summary>Behind and above the hull, looking past it.</summary>
        Chase,

        /// <summary>Behind and above, looking AT the hull from outside.</summary>
        Orbit,

        /// <summary>On the hull's nose, with nothing of the ship in view.</summary>
        Cockpit,

        /// <summary>Beside the docking port, looking back at the approaching ship.</summary>
        Port,
    }

    /// <summary>How far the chase camera sits behind and above the hull, in metres.</summary>
    private const float ChaseDistance = 130f;
    private const float ChaseLift = 42f;
    private const float ChaseLead = 40f;

    /// <summary>How far the port camera stands off the hull.</summary>
    private const float PortStandoff = 230f;

    /// <summary>How far the orbit camera sits from the hull by default, in metres.</summary>
    private const float DefaultOrbitDistance = 260f;

    private const float MinimumOrbit = 25f;
    private const float MaximumOrbit = 4000f;

    /// <summary>Mouse sensitivity, radians per pixel.</summary>
    private const float LookRate = 0.0055f;

    private Mode _mode = Mode.Chase;
    private float _yaw;
    private float _pitch = 0.28f;
    private float _orbitDistance = DefaultOrbitDistance;
    private float _cockpitYaw;
    private float _cockpitPitch;

    /// <summary>
    /// The wheel's own distance for the chase camera, so that zooming in chase does something.
    /// </summary>
    /// <remarks>
    /// Before this the wheel moved <c>_orbitDistance</c> and NOTHING ELSE, and the chase camera
    /// ignored it entirely — so in the default view the wheel did nothing at all, which is exactly
    /// what a player reported. A zoom control that works in one of four modes and says so nowhere is
    /// worse than no zoom control.
    /// </remarks>
    private float _chaseDistance = ChaseDistance;

    internal Mode Current => _mode;

    /// <summary>Starts in a named mode, for rendering a view with no mouse to drag.</summary>
    internal void Use(string name) => _mode = name.ToLowerInvariant() switch
    {
        "orbit" => Mode.Orbit,
        "cockpit" => Mode.Cockpit,
        "port" => Mode.Port,
        _ => Mode.Chase,
    };

    /// <summary>
    /// Puts the camera at an angle and a distance directly, for rendering a look with no mouse.
    /// </summary>
    internal void StartAt(double yawDegrees, double pitchDegrees, double distance)
    {
        _yaw = (float)(yawDegrees * Math.PI / 180.0);
        _cockpitYaw = _yaw;

        if (!double.IsNaN(pitchDegrees))
        {
            _pitch = (float)(pitchDegrees * Math.PI / 180.0);
            _cockpitPitch = _pitch;
        }

        if (distance > 0.0)
        {
            _orbitDistance = (float)distance;
            _chaseDistance = (float)distance;
        }
    }

    internal float OrbitDistance => _orbitDistance;

    /// <summary>Cycles to the next mode.</summary>
    internal void Next()
    {
        _mode = _mode switch
        {
            Mode.Chase => Mode.Orbit,
            Mode.Orbit => Mode.Cockpit,
            Mode.Cockpit => Mode.Port,
            _ => Mode.Chase,
        };
    }

    /// <summary>
    /// Turns the camera, from a mouse delta in pixels.
    /// </summary>
    /// <remarks>
    /// The pitch is clamped just short of the poles. At exactly vertical the up vector and the view
    /// direction are parallel, the cross product that builds the basis is zero, and the frame turns
    /// into a single flat colour — which reads as a rendering failure and is a sign convention.
    /// </remarks>
    internal void Look(float dx, float dy)
    {
        if (_mode == Mode.Cockpit)
        {
            _cockpitYaw -= dx * LookRate;
            _cockpitPitch = Math.Clamp(_cockpitPitch - (dy * LookRate), -1.45f, 1.45f);
            return;
        }

        _yaw -= dx * LookRate;
        _pitch = Math.Clamp(_pitch + (dy * LookRate), -1.45f, 1.45f);
    }

    /// <summary>How far the camera currently is from the hull, for the display.</summary>
    internal float Distance => _mode switch
    {
        Mode.Chase => _chaseDistance,
        Mode.Orbit => _orbitDistance,
        Mode.Port => PortStandoff,
        _ => 0f,
    };

    /// <summary>
    /// Zooms whichever camera is in use.
    /// </summary>
    /// <remarks>
    /// Every mode that has a distance responds, because the alternative is a control that silently
    /// does nothing in three of the four views a player can be in.
    /// </remarks>
    internal void Zoom(float notches)
    {
        float factor = MathF.Pow(1.18f, -notches);

        _orbitDistance = Math.Clamp(_orbitDistance * factor, MinimumOrbit, MaximumOrbit);
        _chaseDistance = Math.Clamp(_chaseDistance * factor, MinimumOrbit, MaximumOrbit);
    }

    /// <summary>
    /// The view matrix, and the basis a star projection needs.
    /// </summary>
    /// <param name="ship">The hull's position in the near pass, which is the origin.</param>
    /// <param name="nose">Unit vector along the hull's nose.</param>
    /// <param name="deck">Unit vector out of the hull's roof.</param>
    /// <param name="portOffset">Where the station's docking port is, in metres, relative to the hull.</param>
    internal (Matrix View, Fix128Vec Forward, Fix128Vec Up) Build(
        Vector3 nose, Vector3 deck, Vector3 portOffset)
    {
        Vector3 eye;
        Vector3 target;
        Vector3 up;

        switch (_mode)
        {
            case Mode.Cockpit:
            {
                // On the nose, a little forward of it, looking wherever the helm is pointed. The ship
                // is not drawn, so this is the view a pilot actually has through the window — and the
                // point of it is that it is the ONLY view in which the reticle means anything.
                eye = nose * 34f;

                Vector3 right = Vector3.Normalize(Vector3.Cross(nose, deck));
                Vector3 look = Vector3.Normalize(
                    (nose * MathF.Cos(_cockpitPitch))
                    + (deck * MathF.Sin(_cockpitPitch))
                    + (right * MathF.Sin(_cockpitYaw)));

                target = eye + look;
                up = deck;
                break;
            }

            case Mode.Port:
            {
                // Beside the docking port, looking back along the corridor at the approaching ship.
                // The camera the docking tests would use if they had one: it shows the corridor, the
                // port and the ship's alignment all at once, which is the whole of what an approach
                // is judged on.
                // Stand off from the port along the corridor and look back down it at the ship.
                // Twenty-six metres put the camera inside the docking funnel, which is a yellow
                // wall and no use to anyone.
                Vector3 port = portOffset;
                Vector3 along = Vector3.Normalize(port);

                float standoff = MathF.Max(420f, port.Length() * 1.6f);

                eye = port + (along * standoff) + (deck * (standoff * 0.35f));
                target = Vector3.Zero;
                up = deck;
                break;
            }

            default:
            {
                float distance = _mode == Mode.Chase ? _chaseDistance : _orbitDistance;

                // The lift stays proportional to the distance, so that zooming out keeps the same
                // angle above the hull rather than sliding the camera down to the axis.
                float lift = _mode == Mode.Chase ? ChaseLift * (_chaseDistance / ChaseDistance) : 0f;

                // Built from the ship's own axes, so rolling the hull rolls the view. Anchoring to a
                // world axis instead makes a barrel roll look like the sky turning, which is
                // disorienting in a way that is hard to attribute to the camera.
                Vector3 right = Vector3.Normalize(Vector3.Cross(nose, deck));

                Vector3 offset = (nose * -MathF.Cos(_pitch) * distance)
                    + (deck * MathF.Sin(_pitch) * distance)
                    + (right * MathF.Sin(_yaw) * distance)
                    + (deck * lift);

                eye = offset;
                target = _mode == Mode.Chase ? nose * ChaseLead : Vector3.Zero;
                up = deck;
                break;
            }
        }

        Matrix view = Matrix.CreateLookAt(eye, target, up);

        // The forward the star projection needs is the direction the CAMERA looks, in the world —
        // which for the near pass is the same frame the hull's axes are expressed in.
        Vector3 forward = Vector3.Normalize(target - eye);

        return (view, ToFix(forward), ToFix(up));
    }

    private static Fix128Vec ToFix(Vector3 v) => new(
        Fix128.FromDouble(v.X), Fix128.FromDouble(v.Y), Fix128.FromDouble(v.Z));

    /// <summary>
    /// A one-line description, for the heads-up display.
    /// </summary>
    /// <remarks>
    /// The angles are on it, and they are there to be a READOUT rather than decoration: "the drag
    /// does nothing" is a report that cannot be told apart from "the drag works and is not obvious"
    /// without seeing what the input did to the state. Two numbers settle it.
    /// </remarks>
    internal string Describe() => _mode switch
    {
        Mode.Chase => $"CHASE {_chaseDistance:F0} m  yaw {Degrees(_yaw):F0}  pitch {Degrees(_pitch):F0}",
        Mode.Orbit => $"ORBIT {_orbitDistance:F0} m  yaw {Degrees(_yaw):F0}  pitch {Degrees(_pitch):F0}",
        Mode.Cockpit => $"COCKPIT  yaw {Degrees(_cockpitYaw):F0}  pitch {Degrees(_cockpitPitch):F0}",
        _ => $"PORT {PortStandoff:F0} m  yaw {Degrees(_yaw):F0}  pitch {Degrees(_pitch):F0}",
    };

    private static float Degrees(float radians) => radians * 180f / MathF.PI;
}
