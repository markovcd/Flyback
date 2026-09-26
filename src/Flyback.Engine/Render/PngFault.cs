namespace Flyback.Core.Render;

/// <summary>Why a picture could not be read, for the complaint that says so.</summary>
public enum PngFault
{
    None,
    Missing,
    NotPng,
    Unsupported,
    Corrupt,
    Empty,
    Elsewhere,
}