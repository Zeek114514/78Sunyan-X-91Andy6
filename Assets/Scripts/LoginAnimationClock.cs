using System;

/// <summary>Advance only visible animation time; never fast-forward through a blocking load.</summary>
public static class LoginAnimationClock
{
    public static float Step(float frameSeconds, bool presented, bool focused, bool ready, float maximumStep)
    {
        if (!presented || !focused || !ready || float.IsNaN(frameSeconds) || float.IsInfinity(frameSeconds) || frameSeconds <= 0)
            return 0;
        if (float.IsNaN(maximumStep) || float.IsInfinity(maximumStep) || maximumStep <= 0) maximumStep = 0.05f;
        return Math.Min(frameSeconds, maximumStep);
    }
}
