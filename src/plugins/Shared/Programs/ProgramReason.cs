using System.Text.RegularExpressions;

namespace Flyback.Plugins.Programs;

/// <summary>Why a program failed, in words a person can act on.</summary>
internal static class ProgramReason
{
    /// <summary>
    /// What it said, or what it wrote to its error stream, with the one thing a
    /// person can do about being signed out said outright.
    /// </summary>
    /// <param name="said">Its own account of the failure, where it gave one.</param>
    /// <param name="errors">What it wrote to its error stream.</param>
    /// <param name="name">What the person calls the program.</param>
    /// <param name="signedOut">Matches a reason that means nobody is signed in.</param>
    /// <param name="signIn">What to do about it, as a sentence.</param>
    public static string Explained(string said, string errors, string name, Regex signedOut, string signIn)
    {
        var reason = !string.IsNullOrWhiteSpace(said) ? said.Trim() : errors.Trim();

        if (reason.Length > 600) reason = reason[..600] + "…";

        if (signedOut.IsMatch(reason)) return signIn + " " + reason;

        return reason.Length > 0 ? $"{name} failed: {reason}" : $"{name} exited without answering.";
    }
}
