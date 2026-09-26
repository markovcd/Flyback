namespace Flyback.Plugins.Assist;

/// <summary>
/// One model a provider suggests, and what it will accept being handed.
/// </summary>
/// <remarks>
/// A suggestion, not a whitelist: anything may be typed, because
/// <see cref="AssistantSchema.BaseUrlEditable"/> means the endpoint may be one
/// nobody here has heard of. Nothing outside a plugin reads this — the shell must
/// not know one model name from another (ADR-0069).
/// </remarks>
/// <param name="Id">What goes in the request.</param>
/// <param name="Vision">Whether it accepts a picture. Nearly all of them do.</param>
/// <param name="Hearing">
/// Whether it accepts a sound. Most do not — see
/// <see cref="AssistantChoices.Hearing"/>. True and <paramref name="Vision"/>
/// true is the case everything downstream turns on: such a model drives the
/// conversation and is played the clip itself, so the run has no second model and
/// no <see cref="AssistantChoices.EarModel"/>.
/// </param>
public sealed record AssistantModel(string Id, bool Vision = true, bool Hearing = false);