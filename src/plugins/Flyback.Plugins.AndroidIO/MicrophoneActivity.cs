using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Flyback.Plugins.AndroidIO;

/// <summary>An activity with nothing on it, which exists to put Android's microphone question to the person.</summary>
[Activity(Theme = "@android:style/Theme.Translucent.NoTitleBar", Exported = false, NoHistory = true, ExcludeFromRecents = true)]
public sealed class MicrophoneActivity : Activity
{
    private const int Request = 1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestPermissions([Manifest.Permission.RecordAudio], Request);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        Microphone.Answered(grantResults is [Permission.Granted, ..]);
        Finish();
    }

    protected override void OnDestroy()
    {
        // Dismissed without an answer, as by the back button, is a refusal.
        Microphone.Answered(Microphone.Granted);
        base.OnDestroy();
    }
}
