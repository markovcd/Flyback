using Android;
using Android.Content;
using Android.Content.PM;

namespace Flyback.Plugins.AndroidIO;

/// <summary>Whether the app may listen to the microphone, asked of the person when it has not been yet.</summary>
public static class Microphone
{
    private static readonly Lock Gate = new();
    private static TaskCompletionSource<bool>? asking;

    public static bool Granted =>
        global::Android.App.Application.Context.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted;

    /// <summary>True once the person has allowed it, false if they refused; asks at most once at a time.</summary>
    public static Task<bool> Ask()
    {
        if (Granted) return Task.FromResult(true);

        lock (Gate)
        {
            if (asking is { } already) return already.Task;

            asking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MicrophoneActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.NoAnimation);
        context.StartActivity(intent);

        return asking.Task;
    }

    /// <summary>What the person answered, from <see cref="MicrophoneActivity"/>.</summary>
    internal static void Answered(bool granted)
    {
        TaskCompletionSource<bool>? waiting;

        lock (Gate)
        {
            waiting = asking;
            asking = null;
        }

        waiting?.TrySetResult(granted);
    }
}
