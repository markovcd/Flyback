using Android;
using Android.App;

// Merged into the app's manifest, so the microphone can be asked for at all.
[assembly: UsesPermission(Manifest.Permission.RecordAudio)]
