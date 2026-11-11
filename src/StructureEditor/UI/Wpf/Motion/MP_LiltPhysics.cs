using System.Diagnostics;
using W = System.Windows;
using WM = System.Windows.Media;
using WC = System.Windows.Controls;

namespace StructureEditor;

// Physics and cursor mechanics adapted from lilt (MIT, 2026 chhongzh):
// https://github.com/chhongzh/lilt/blob/dev/anim/value.go
// https://github.com/chhongzh/lilt/blob/dev/anim/spring.go
// https://github.com/chhongzh/lilt/blob/dev/ui/cursor.go
// See docs/third-party/lilt.md for the license and attribution.
internal readonly record struct MP_LiltSpring(double Response, double Damping, bool Ease = false)
{
    internal static readonly MP_LiltSpring Default = new(.29, .75);
    internal static readonly MP_LiltSpring Smooth = new(.39, .85);
    internal static readonly MP_LiltSpring Snappy = new(.31, .8);
    internal static readonly MP_LiltSpring Bouncy = new(.25, .45);
    internal static readonly MP_LiltSpring Interactive = new(.14, 1);
    internal static readonly MP_LiltSpring CursorLead = new(.22, 1, true);
    internal static readonly MP_LiltSpring CursorTrail = new(.52, 1, true);
    internal static readonly MP_LiltSpring CursorRelease = new(.57, .4);

    internal double Rate => 2 * Math.PI / Math.Max(.001, Response);

    // lilt anim.RubberBand, using UIScrollView's 0.55 resistance.
    internal static double RubberBand(double raw, double dimension)
    {
        if (dimension <= 0 || raw == 0) return 0;
        double magnitude = Math.Abs(raw);
        double resisted = (1 - 1 / (magnitude * .55 / dimension + 1)) * dimension;
        return Math.CopySign(resisted, raw);
    }
}

// Target and velocity never reset on retarget. Resting values are removed
// from the frame scheduler, so an idle editor has no animation loop.
internal sealed class MP_LiltValue
{
    internal double Current { get; private set; }
    internal double Target { get; private set; }
    internal double Velocity { get; private set; }
    internal double Precision { get; set; } = .05;
    internal MP_LiltSpring Spring { get; set; }
    internal Action Changed;
    internal bool Active { get; private set; }

    internal MP_LiltValue(double current, MP_LiltSpring spring, Action changed = null)
    {
        Current = Target = current;
        Spring = spring;
        Changed = changed;
    }

    internal void Set(double target)
    {
        if (!double.IsFinite(target) || Math.Abs(Target - target) < .000001) return;
        Target = target;
        if (!MP_Motion.Enabled) { Jump(target); return; }
        Active = true;
        MP_LiltClock.Activate(this);
    }

    internal void Jump(double value)
    {
        Current = Target = value;
        Velocity = 0;
        Active = false;
        MP_LiltClock.Deactivate(this);
        Changed?.Invoke();
    }

    // lilt Value.Shift: keep the target unchanged, let the row spring back.
    internal void Shift(double distance, double maxOffset = double.PositiveInfinity)
    {
        if (!MP_Motion.Enabled || Math.Abs(distance) < .001) return;
        // Keep row movement below 3px so independently animated rows never
        // cover each other or another folder heading on a desktop screen.
        Current = Math.Clamp(Current + distance, -maxOffset, maxOffset);
        Active = true;
        MP_LiltClock.Activate(this);
        Changed?.Invoke();
    }

    internal void Kick(double velocity)
    {
        if (!MP_Motion.Enabled) return;
        Velocity += velocity;
        Active = true;
        MP_LiltClock.Activate(this);
    }

    internal void Bump(double pixels) => Kick(pixels * Spring.Rate * Math.E);

    internal void Step(double dt)
    {
        if (!Active) return;
        double displacement = Current - Target;
        double velocity = Velocity;
        double rate = Spring.Rate;
        if (Spring.Ease)
        {
            displacement *= Math.Exp(-rate * dt);
            velocity = 0;
        }
        else
        {
            double stiffness = rate * rate;
            double drag = 2 * Spring.Damping * rate;
            // Match lilt: semi-implicit Euler with <=1/240 s substeps.
            int steps = Math.Max(1, (int)(dt * 240) + 1);
            double step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += (-stiffness * displacement - drag * velocity) * step;
                displacement += velocity * step;
            }
        }

        double precision = Math.Max(.0001, Precision);
        if (Math.Abs(displacement) < precision && Math.Abs(velocity) < precision * 10)
        {
            Current = Target;
            Velocity = 0;
            Active = false;
        }
        else
        {
            Current = Target + displacement;
            Velocity = velocity;
        }
        Changed?.Invoke();
    }
}

internal static class MP_LiltClock
{
    private static readonly HashSet<MP_LiltValue> Active = new();
    private static long lastFrame;
    private static bool subscribed;
    internal static bool Busy => Active.Count > 0;

    internal static void Activate(MP_LiltValue value)
    {
        Active.Add(value);
        if (subscribed) return;
        subscribed = true;
        lastFrame = Stopwatch.GetTimestamp();
        WM.CompositionTarget.Rendering += Frame;
    }

    internal static void Deactivate(MP_LiltValue value)
    {
        Active.Remove(value);
        if (Active.Count == 0) Stop();
    }

    private static void Frame(object sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp((now - lastFrame) / (double)Stopwatch.Frequency,
            0, .05);
        lastFrame = now;
        if (!MP_Motion.Enabled) { Stop(); return; }
        foreach (MP_LiltValue item in Active.ToArray())
        {
            item.Step(dt);
            if (!item.Active) Active.Remove(item);
        }
        if (Active.Count == 0) Stop();
    }

    private static void Stop()
    {
        if (!subscribed) return;
        WM.CompositionTarget.Rendering -= Frame;
        subscribed = false;
    }
}

// lilt's travelling rectangle: the leading edge runs ahead of the trailing
// edge. Retargeting keeps each edge's current position and spring history.
internal sealed class MP_LiltCursor
{
    private readonly WC.Border visual;
    private readonly bool horizontal;
    private readonly WM.TranslateTransform offset;
    private readonly MP_LiltValue leading, trailing, squeeze;
    private bool initialized;

    internal MP_LiltCursor(WC.Border visual, bool horizontal)
    {
        this.visual = visual;
        this.horizontal = horizontal;
        offset = visual.RenderTransform as WM.TranslateTransform
            ?? new WM.TranslateTransform();
        visual.RenderTransform = offset;
        leading = new MP_LiltValue(0, MP_LiltSpring.CursorLead, Draw);
        trailing = new MP_LiltValue(0, MP_LiltSpring.CursorLead, Draw);
        squeeze = new MP_LiltValue(0, MP_LiltSpring.Interactive, Draw)
        { Precision = .005 };
    }

    internal void Move(double start, double length, bool immediate = false)
    {
        if (!double.IsFinite(start) || !double.IsFinite(length)) return;
        length = Math.Max(1, length);
        if (!initialized)
        {
            initialized = true;
            if (immediate || !MP_Motion.Enabled)
            {
                leading.Jump(start);
                trailing.Jump(start + length);
                return;
            }
            double midpoint = start + length / 2;
            leading.Jump(midpoint);
            trailing.Jump(midpoint);
            // Animated first focus grows out of its target's centre.
        }
        else if (immediate || !MP_Motion.Enabled)
        {
            leading.Jump(start);
            trailing.Jump(start + length);
            return;
        }
        double oldMiddle = (leading.Target + trailing.Target) / 2;
        double newMiddle = start + length / 2;
        leading.Spring = newMiddle >= oldMiddle
            ? MP_LiltSpring.CursorTrail : MP_LiltSpring.CursorLead;
        trailing.Spring = newMiddle >= oldMiddle
            ? MP_LiltSpring.CursorLead : MP_LiltSpring.CursorTrail;
        leading.Set(start);
        trailing.Set(start + length);
        Draw();
    }

    internal void Press()
    {
        squeeze.Spring = MP_LiltSpring.Interactive;
        squeeze.Set(1);
    }

    internal void Release()
    {
        squeeze.Spring = MP_LiltSpring.CursorRelease;
        squeeze.Set(0);
    }

    private void Draw()
    {
        if (!initialized) return;
        double inset = squeeze.Current * (horizontal ? 3 : 1.5);
        double left = leading.Current + inset;
        double right = trailing.Current - inset;
        double min = Math.Min(left, right), max = Math.Max(left, right);
        if (horizontal)
        {
            offset.X = min;
            visual.Width = Math.Max(1, max - min);
        }
        else
        {
            offset.Y = min;
            visual.Height = Math.Max(1, max - min);
        }
    }
}

// Scrolling also follows lilt's spring scheduler instead of restarting
// fixed-duration DoubleAnimations for every wheel notch.
internal sealed class MP_LiltScroll
{
    private readonly MP_ScrollOffsetProxy proxy;
    private readonly MP_LiltValue position;
    private readonly MP_LiltValue edge;
    private readonly WM.TranslateTransform elasticTransform;

    internal MP_LiltScroll(MP_ScrollOffsetProxy proxy, W.UIElement elasticSurface = null)
    {
        this.proxy = proxy;
        position = new MP_LiltValue(proxy.Offset, MP_LiltSpring.Snappy);
        position.Changed = () => this.proxy.Offset = position.Current;
        if (elasticSurface != null)
        {
            elasticTransform = elasticSurface.RenderTransform as WM.TranslateTransform
                ?? new WM.TranslateTransform();
            elasticSurface.RenderTransform = elasticTransform;
            edge = new MP_LiltValue(0, MP_LiltSpring.Bouncy);
            edge.Changed = () => elasticTransform.Y = edge.Current;
        }
    }

    internal void Nudge(double rawOverrun, double viewportHeight)
    {
        if (edge == null || !MP_Motion.Enabled || Math.Abs(rawOverrun) < .001)
            return;
        double resisted = MP_LiltSpring.RubberBand(rawOverrun, viewportHeight);
        // Content responds opposite to the requested scroll direction.
        edge.Bump(Math.Clamp(-resisted * .085, -6, 6));
    }

    internal void To(double offset)
    {
        if (!double.IsFinite(offset)) return;
        proxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        // Keyboard ScrollIntoView and mouse-drag scrollbars may move the
        // ScrollViewer outside the spring; synchronize when at rest.
        if (!position.Active && Math.Abs(proxy.Offset - position.Current) > .25)
            position.Jump(proxy.Offset);
        position.Set(offset);
    }

    internal void Jump(double offset)
    {
        proxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        position.Jump(offset);
        edge?.Jump(0);
    }
}

// Port of lilt widget.Scroll.wave: each row gets a response that grows with
// its distance from the selected row, giving the scrolling list a soft wave.
// RenderTransforms avoid relayout and never affect the underlying item model.
internal sealed class MP_LiltWaveScroll
{
    private sealed class RowState
    {
        internal MP_LiltValue Value;
        internal WM.TranslateTransform Transform;
    }
    private readonly Dictionary<W.UIElement, RowState> tracked = new();

    internal void Shift(IEnumerable<W.UIElement> rows, int focus, double delta)
    {
        if (!MP_Motion.Enabled || Math.Abs(delta) < .01) return;
        int index = 0;
        foreach (var row in rows)
        {
            if (!row.IsVisible)
            {
                index++;
                continue;
            }
            if (!tracked.TryGetValue(row, out RowState state))
            {
                var transform = row.RenderTransform as WM.TranslateTransform
                    ?? new WM.TranslateTransform();
                row.RenderTransform = transform;
                state = new RowState { Transform = transform };
                RowState captured = state;
                state.Value = new MP_LiltValue(0, MP_LiltSpring.Default,
                    () => captured.Transform.Y = captured.Value.Current);
                tracked[row] = state;
            }
            int distance = focus < 0 ? 0 : Math.Min(7, Math.Abs(index - focus));
            state.Value.Spring = new MP_LiltSpring(
                MP_LiltSpring.Default.Response * (1 + .2 * distance),
                MP_LiltSpring.Default.Damping);
            // The wave is subpixel on desktop; clip margins remain intact
            // even when neighbouring rows travel in opposite directions.
            state.Value.Shift(Math.Clamp(delta * .03, -.4, .4), .7);
            index++;
        }
    }

    internal void Reset()
    {
        foreach (var state in tracked.Values)
            state.Value.Jump(0);
        tracked.Clear();
    }
}
