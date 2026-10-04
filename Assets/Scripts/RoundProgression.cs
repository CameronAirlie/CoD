using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Local progression is saved only after a completed match, independently of match authority.</summary>
public sealed class RoundProgression : ScriptBehaviour
{
    private MultiplayerSession? _session;
    private MatchPhase? _phase;
    public int LastMatchExperience { get; private set; }
    public int TotalExperience { get; private set; }
    public int Level => 1 + TotalExperience / 1000;
    public override void OnCreate()
    {
        try
        {
            if (ProjectStorage.UserDataFileExists("progression.json"))
                TotalExperience = Math.Clamp(ProjectStorage.ReadUserDataJson<Progress>("progression.json")?.Experience ?? 0, 0, 100000000);
        }
        catch (Exception error) { Debug.LogWarning($"Could not load progression: {error.Message}"); }
        _session = GameObject.GetComponent<MultiplayerSession>();
        if (_session is not null) _session.MatchUpdated += OnMatch;
    }
    public override void OnDestroy() { if (_session is not null) _session.MatchUpdated -= OnMatch; }
    private void OnMatch(MatchSnapshot match)
    {
        if (_phase == MatchPhase.Playing && match.Phase == MatchPhase.Results && (match.Mode != MatchMode.Defusal || match.Defusal?.MatchOver == true))
        {
            var local = Array.Find(match.Players, p => p.PeerId == match.LocalPeerId);
            if (local is not null)
            {
                var own = local.Team == PlayerTeam.Alpha ? match.AlphaScore : match.BravoScore;
                var other = local.Team == PlayerTeam.Alpha ? match.BravoScore : match.AlphaScore;
                LastMatchExperience = CombatFeedbackMath.RoundExperience(local, own > other);
                TotalExperience = Math.Min(100000000, TotalExperience + LastMatchExperience);
                try { ProjectStorage.WriteUserDataJson("progression.json", new Progress(TotalExperience)); }
                catch (Exception error) { Debug.LogWarning($"Could not save progression: {error.Message}"); }
            }
        }
        _phase = match.Phase;
    }
    private sealed record Progress(int Experience);
}
