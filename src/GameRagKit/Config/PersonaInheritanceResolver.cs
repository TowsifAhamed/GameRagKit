namespace GameRagKit.Config;

/// <summary>
/// The fully-resolved persona for one NPC after walking the world -> region -> faction ->
/// npc inheritance chain and merging every tier's persona.yaml fragment (see
/// PersonaFragment) with the NPC's own persona: block. Lets a game dev write a shared
/// personality once at whichever tier makes sense (e.g. every NPC in a faction) and have
/// many NPCs inherit it without copy-pasting into each NPC's own YAML file -- an NPC file
/// can be as small as an id + faction_id and inherit everything else.
/// </summary>
public sealed record ResolvedPersona(
    string SystemPrompt,
    string? Style,
    IReadOnlyList<string> Traits,
    IReadOnlyList<ActionDefinition> Actions,
    bool MoodTracking);

public static class PersonaInheritanceResolver
{
    private const string FragmentFileName = "persona.yaml";

    /// <summary>
    /// Reads persona.yaml from world/, region/&lt;id&gt;/, and faction/&lt;id&gt;/ under
    /// configDirectory (whichever exist -- all are optional), then merges them with the
    /// NPC's own persona config in world -> region -> faction -> npc order:
    /// system_prompt fragments concatenate in that order (each tier's text is appended,
    /// not replaced, so a more specific tier adds to its parents' context rather than
    /// erasing it); style/traits/actions/mood_tracking use the most specific non-null
    /// value, falling back up the chain when a tier doesn't set one, the same way single-
    /// NPC config already works for fields like style today.
    /// </summary>
    public static ResolvedPersona Resolve(string configDirectory, PersonaConfig persona)
    {
        var fragments = new List<PersonaFragment>();

        if (!string.IsNullOrWhiteSpace(persona.WorldId))
        {
            AddFragmentIfExists(fragments, configDirectory, "world", persona.WorldId);
        }

        if (!string.IsNullOrWhiteSpace(persona.RegionId))
        {
            AddFragmentIfExists(fragments, configDirectory, "region", persona.RegionId);
        }

        if (!string.IsNullOrWhiteSpace(persona.FactionId))
        {
            AddFragmentIfExists(fragments, configDirectory, "faction", persona.FactionId);
        }

        var systemPromptParts = fragments
            .Select(f => f.SystemPrompt)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .ToList();

        if (!string.IsNullOrWhiteSpace(persona.SystemPrompt))
        {
            systemPromptParts.Add(persona.SystemPrompt.Trim());
        }

        var systemPrompt = string.Join("\n\n", systemPromptParts);

        var style = persona.Style;
        for (var i = fragments.Count - 1; i >= 0 && string.IsNullOrWhiteSpace(style); i--)
        {
            style = fragments[i].Style;
        }
        if (string.IsNullOrWhiteSpace(style))
        {
            style = "concise";
        }

        var traits = new List<string>();
        foreach (var fragment in fragments)
        {
            traits.AddRange(fragment.Traits);
        }
        traits.AddRange(persona.Traits);

        var actions = new List<ActionDefinition>();
        foreach (var fragment in fragments)
        {
            actions.AddRange(fragment.Actions);
        }
        actions.AddRange(persona.Actions);

        var moodTracking = persona.MoodTracking;
        if (!moodTracking)
        {
            for (var i = fragments.Count - 1; i >= 0; i--)
            {
                if (fragments[i].MoodTracking == true)
                {
                    moodTracking = true;
                    break;
                }
            }
        }

        return new ResolvedPersona(systemPrompt, style, traits, actions, moodTracking);
    }

    private static void AddFragmentIfExists(List<PersonaFragment> fragments, string configDirectory, string tier, string id)
    {
        var path = Path.Combine(configDirectory, tier, id, FragmentFileName);
        if (!File.Exists(path))
        {
            return;
        }

        var yaml = File.ReadAllText(path);
        fragments.Add(PersonaFragment.LoadFromYaml(yaml));
    }
}
