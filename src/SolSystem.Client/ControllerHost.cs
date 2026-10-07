using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Client;

/// <summary>
/// The narrow surface the controller drives: what an external pilot may read and command.
/// </summary>
/// <remarks>
/// <para>
/// This interface is the wall between HTTP and the game. The host never sees a field of
/// <see cref="FlightGame"/>'s, only these members — the same discipline the renderers follow
/// when they read the session. Anything not on this list is not part of the API, on purpose.
/// </para>
/// <para>
/// Every method here runs ON the game's update, at a tick boundary, because that is the only
/// place the simulation may be touched. The host merely queues.
/// </para>
/// </remarks>
internal interface IControllerTarget
{
    /// <summary>The clock, as a Julian date.</summary>
    internal double JulianDate { get; }

    /// <summary>How many 120 Hz ticks the world has stepped since the run began.</summary>
    internal long WorldTicks { get; }

    /// <summary>The station's offset from its host, metres.</summary>
    internal Fix128Vec StationOffset { get; }

    /// <summary>The station's velocity relative to its host, metres per second.</summary>
    internal Fix128Vec StationVelocity { get; }

    /// <summary>The ship, as a snapshot. Metres, Earth-centred, the simulation's own frame.</summary>
    internal Ship Ship { get; }

    /// <summary>The throttle the lever is at, 0 to 1.</summary>
    internal double Throttle { get; }

    /// <summary>Whether the flight computer holds the controls.</summary>
    internal bool HelmEngaged { get; }

    /// <summary>
    /// The world hash: SHA-256 over the same raw fixed-point words the probe hashes.
    /// </summary>
    internal string WorldHash();

    /// <summary>Steps the world N navigation ticks, under the current throttle or the helm.</summary>
    internal void AdvanceTicks(int ticks);

    /// <summary>Sets the throttle the lever rests at, 0 to 1.</summary>
    internal void SetThrottle(double fraction);

    /// <summary>
    /// Engages the autohelm on a course to a body, as the chart's ENTER does.
    /// </summary>
    /// <remarks>Returns null on success, or the reason it was refused.</remarks>
    internal string? Engage(string body, string? optionName);

    /// <summary>
    /// Begins a render run: the next N frames are drawn through the normal loop and saved as
    /// numbered PNGs, one per frame, into the directory.
    /// </summary>
    /// <remarks>Returns false when a render run is already in progress.</remarks>
    internal bool BeginRender(int frames, string directory);

    /// <summary>Closes the client. The driver asked for it, as Escape does.</summary>
    internal void Quit();
}

/// <summary>
/// The controller: HTTP on loopback, into the running client, one request per update.
/// </summary>
/// <remarks>
/// <para>
/// The design rules are §12.1's and they are all load-bearing. The listener thread owns
/// nothing but the queue — it never touches the simulation, the renderer, or the game's
/// fields. The game's update drains at most one request, executes it at the tick boundary,
/// and the HTTP handler awaits the answer. A driver therefore drives the same loop a player
/// flies, and can verify it is the same world by hashing it.
/// </para>
/// <para>
/// A render request is the one response that is not immediate: the frames are drawn across
/// the following N updates, and the parked request is answered when the last frame is saved.
/// The parked request lives here, and the game hands the result back through
/// <see cref="RenderFinished"/>.
/// </para>
/// </remarks>
internal sealed class ControllerHost : IDisposable
{
    /// <summary>How long a handler waits before declaring the game unresponsive.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The tick counts an /advance may ask for. A runaway request is bounded.</summary>
    private const int MaxAdvanceTicks = 1_000_000;

    private const int MaxRenderFrames = 100_000;

    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<ControllerJob> _queue = new();
    private readonly CancellationTokenSource _cancelled = new();
    private ControllerJob? _parkedRender;

    private volatile IControllerTarget? _target;

    private ControllerHost(int port) => Port = port;

    /// <summary>The port the listener bound.</summary>
    internal int Port { get; }

    /// <summary>
    /// Opens the listener and starts accepting. The game has already loaded its content,
    /// so a request that arrives at frame one finds a world to answer about.
    /// </summary>
    internal static ControllerHost Start(int port, IControllerTarget target)
    {
        var host = new ControllerHost(port);
        host._target = target;
        host._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        host._listener.Start();
        Console.WriteLine($"  controller on http://127.0.0.1:{port}/ (one request per update)");
        _ = Task.Run(host.AcceptLoop);
        return host;
    }

    private async Task AcceptLoop()
    {
        while (!_cancelled.IsCancellationRequested)
        {
            HttpListenerContext? context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception)
            {
                // A cancelled shutdown closes the listener under a pending accept; the
                // ObjectDisposedException and HttpListenerException that raises is how this
                // loop is told to stop, not a fault.
                if (!_cancelled.IsCancellationRequested)
                {
                    Console.WriteLine($"  controller listener stopped: {exception.Message}");
                }

                return;
            }

            _ = Task.Run(() => Handle(context));
        }
    }

    /// <summary>
    /// Drains at most one queued request, on the game's thread, at a tick boundary.
    /// </summary>
    /// <remarks>Returns whether a request was executed — which is also how the driven
    /// update knows its frame had business to do.</remarks>
    internal bool DrainOne(IControllerTarget target)
    {
        if (!_queue.TryDequeue(out ControllerJob? job))
        {
            return false;
        }

        try
        {
            string? response = job.Work(target);
            if (response is not null)
            {
                job.Answer.TrySetResult(response);
            }
        }
        catch (Exception exception)
        {
            job.Answer.TrySetResult(Error(exception.Message));
        }

        return true;
    }

    /// <summary>
    /// The game finished the last frame of a render run; the parked request gets its answer.
    /// </summary>
    internal void RenderFinished(int frames, string directory, string lastFile)
    {
        if (_parkedRender is { } render)
        {
            _parkedRender = null;
            render.Answer.TrySetResult(RenderResult(frames, directory, lastFile));
        }
    }

    public void Dispose()
    {
        _cancelled.Cancel();

        try
        {
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed; the shutdown path races itself only when quitting twice.
        }

        while (_queue.TryDequeue(out ControllerJob? job))
        {
            job.Answer.TrySetResult(Error("the client shut down before this request ran"));
        }

        if (_parkedRender is { } render)
        {
            render.Answer.TrySetResult(Error("the client shut down mid-render"));
        }

        _cancelled.Dispose();
    }

    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            string body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            (int status, string json) = await RouteAsync(context.Request.HttpMethod,
                context.Request.Url?.AbsolutePath ?? "/", body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            byte[] payload = Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload);
        }
        catch (Exception exception)
        {
            try
            {
                context.Response.StatusCode = 500;
                byte[] payload = Encoding.UTF8.GetBytes(Error(exception.Message));
                context.Response.ContentLength64 = payload.Length;
                context.Response.OutputStream.Write(payload);
            }
            catch (Exception)
            {
                // The connection may already be gone; the request fails loudly enough there.
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task<(int, string)> RouteAsync(string method, string path, string body)
    {
        if (_target is not { } target)
        {
            return (503, Error("the game has not loaded its world yet"));
        }

        try
        {
            switch (path, method)
            {
                case ("/state", "GET"):
                    return (200, await Enqueue(t => StateJson(t)));

                case ("/hash", "GET"):
                    return (200, await Enqueue(t => HashJson(t)));

                case ("/advance", "POST"):
                {
                    using var document = JsonDocument.Parse(body);
                    int ticks = AdvanceTicks(document.RootElement);
                    return (200, await Enqueue(t =>
                    {
                        t.AdvanceTicks(ticks);
                        return StateJson(t);
                    }));
                }

                case ("/throttle", "POST"):
                {
                    using var document = JsonDocument.Parse(body);
                    double fraction = RequireNumber(document.RootElement, "fraction", 0.0, 1.0);
                    return (200, await Enqueue(t =>
                    {
                        t.SetThrottle(fraction);
                        return JsonSerializer.Serialize(new { throttle = t.Throttle });
                    }));
                }

                case ("/engage", "POST"):
                {
                    using var document = JsonDocument.Parse(body);
                    string bodyName = RequireString(document.RootElement, "body");
                    string? optionName = OptionalString(document.RootElement, "option");
                    return (200, await Enqueue(t =>
                    {
                        string? refused = t.Engage(bodyName, optionName);
                        return refused is null
                            ? JsonSerializer.Serialize(new { engaged = bodyName })
                            : Error(refused);
                    }));
                }

                case ("/render", "POST"):
                {
                    using var document = JsonDocument.Parse(body);
                    int frames = (int)RequireNumber(document.RootElement, "frames", 1.0, MaxRenderFrames);
                    string directory = RequireString(document.RootElement, "dir");
                    var job = new ControllerJob
                    {
                        Work = t =>
                        {
                            if (!t.BeginRender(frames, directory))
                            {
                                return Error("a render run is already in progress");
                            }

                            // The answer comes when the last frame is saved.
                            return null;
                        },
                        Answer = new(TaskCreationOptions.RunContinuationsAsynchronously),
                    };
                    _parkedRender = job;
                    _queue.Enqueue(job);
                    string json = await AwaitOrTimeout(job.Answer.Task);
                    return (200, json);
                }

                case ("/quit", "POST"):
                {
                    string json = await Enqueue(t =>
                    {
                        t.Quit();
                        return JsonSerializer.Serialize(new { quitting = true });
                    });
                    return (200, json);
                }

                default:
                    return (404, Error($"no such endpoint '{method} {path}'"));
            }
        }
        catch (JsonException exception)
        {
            return (400, Error($"bad JSON: {exception.Message}"));
        }
        catch (ArgumentException exception)
        {
            return (400, Error(exception.Message));
        }
    }

    /// <summary>
    /// Queues a request and waits for the game's update to answer it.
    /// </summary>
    /// <remarks>
    /// The HTTP thread blocks here — which is the design working, not a deadlock: the world
    /// advances exactly while the driver waits for its answer, and two drivers queue honestly
    /// because the game drains one request per update.
    /// </remarks>
    private async Task<string> Enqueue(Func<IControllerTarget, string?> work)
    {
        var job = new ControllerJob
        {
            Work = work,
            Answer = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        _queue.Enqueue(job);
        return await AwaitOrTimeout(job.Answer.Task);
    }

    private async Task<string> AwaitOrTimeout(Task<string> answer)
    {
        Task completed = await Task.WhenAny(answer, Task.Delay(AnswerTimeout));
        if (completed != answer)
        {
            return Error("the game did not answer in 60 s; is its window still being serviced?");
        }

        return await answer;
    }

    private static string StateJson(IControllerTarget target)
    {
        Ship ship = target.Ship;
        return JsonSerializer.Serialize(new
        {
            julianDate = target.JulianDate,
            ticks = target.WorldTicks,
            station = new
            {
                offset = Vec(target.StationOffset),
                velocity = Vec(target.StationVelocity),
            },
            ship = new
            {
                position = Vec(ship.Position),
                velocity = Vec(ship.Velocity),
                nose = Vec(ship.Attitude.Forward),
                massTonnes = ship.Mass.ToDouble(),
                propellantTonnes = ship.Propellant.ToDouble(),
                throttle = target.Throttle,
                helmEngaged = target.HelmEngaged,
            },
        });
    }

    private static string HashJson(IControllerTarget target) => JsonSerializer.Serialize(new
    {
        ticks = target.WorldTicks,
        hash = target.WorldHash(),
    });

    private static string RenderResult(int frames, string directory, string lastFile) =>
        JsonSerializer.Serialize(new
        {
            frames,
            directory,
            last = lastFile,
        });

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });

    private static object Vec(Fix128Vec vector) => new
    {
        x = vector.X.ToDouble(),
        y = vector.Y.ToDouble(),
        z = vector.Z.ToDouble(),
    };

    private static int AdvanceTicks(JsonElement element)
    {
        if (element.TryGetProperty("ticks", out JsonElement ticksElement))
        {
            return ClampTicks(NumberValue(ticksElement, "ticks", 1.0, MaxAdvanceTicks));
        }

        if (element.TryGetProperty("seconds", out JsonElement secondsElement))
        {
            double seconds = NumberValue(secondsElement, "seconds",
                Constants.NavigationTickSeconds, double.MaxValue);
            return ClampTicks(Math.Round(seconds / Constants.NavigationTickSeconds));
        }

        // Days are the driver's unit for anything longer than minutes; the same conversion
        // the probe's `days` command makes.
        double days = RequireNumber(element, "days", double.Epsilon, double.MaxValue);
        return ClampTicks(Math.Round(days * 86400.0 / Constants.NavigationTickSeconds));
    }

    private static int ClampTicks(double ticks) => (int)Math.Clamp(Math.Round(ticks), 1, MaxAdvanceTicks);

    private static double RequireNumber(JsonElement element, string name, double min, double max)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number)
        {
            throw new ArgumentException($"expected a number '{name}'");
        }

        return NumberValue(value, name, min, max);
    }

    private static double NumberValue(JsonElement value, string name, double min, double max)
    {
        if (!value.TryGetDouble(out double number))
        {
            throw new ArgumentException($"expected a number '{name}'");
        }

        if (number < min || number > max)
        {
            throw new ArgumentException($"'{name}' wants {min} to {max}, got {number}");
        }

        return number;
    }

    private static string RequireString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"expected a string '{name}'");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>One queued request: the work to run at the boundary, and who waits for it.</summary>
    private sealed class ControllerJob
    {
        internal required Func<IControllerTarget, string?> Work { get; init; }

        internal required TaskCompletionSource<string> Answer { get; init; }
    }
}
